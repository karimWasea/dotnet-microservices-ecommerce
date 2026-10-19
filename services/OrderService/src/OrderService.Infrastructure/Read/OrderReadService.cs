using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Abstractions;
using OrderService.Application.Orders.DTOs;
using OrderService.Infrastructure.Read.Models;

namespace OrderService.Infrastructure.Read;

public sealed class OrderReadService : IOrderReadService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderReadDbContext _readDbContext;

    public OrderReadService(OrderReadDbContext readDbContext)
    {
        _readDbContext = readDbContext;
    }

    public async Task<OrderDetailsDto?> GetByIdAsync(Guid orderId, CancellationToken ct = default)
    {
        var row = await _readDbContext.OrderDetails
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (row is null)
        {
            return null;
        }

        var address = JsonSerializer.Deserialize<AddressDto>(row.ShippingAddressJson, JsonOptions)
            ?? new AddressDto("", "", "", "");
        var items = JsonSerializer.Deserialize<List<OrderLineDto>>(row.ItemsJson, JsonOptions)
            ?? [];
        var history = JsonSerializer.Deserialize<List<StatusHistoryEntryDto>>(row.HistoryJson, JsonOptions)
            ?? [];

        return new OrderDetailsDto(
            row.Id,
            row.UserId,
            row.Status,
            row.StatusReason,
            row.CancelledBy,
            row.PaymentId,
            row.Total,
            row.Currency,
            row.ItemCount,
            address,
            items,
            history,
            row.PlacedAtUtc,
            row.UpdatedAtUtc,
            row.Version);
    }

    public async Task<PagedResult<OrderSummaryDto>> ListByUserAsync(
        Guid userId,
        int page,
        int pageSize,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = _readDbContext.OrderSummaries
            .AsNoTracking()
            .Where(x => x.UserId == userId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim();
            query = query.Where(x => x.Status.ToLower() == normalized.ToLower());
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.PlacedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => MapToSummaryDto(x))
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryDto>(items, page, pageSize, totalCount);
    }

    public async Task<PagedResult<OrderSummaryDto>> ListAllAsync(
        int page,
        int pageSize,
        Guid? userId = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var query = _readDbContext.OrderSummaries.AsNoTracking();

        if (userId.HasValue && userId.Value != Guid.Empty)
        {
            query = query.Where(x => x.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = status.Trim();
            query = query.Where(x => x.Status.ToLower() == normalized.ToLower());
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.PlacedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => MapToSummaryDto(x))
            .ToListAsync(ct);

        return new PagedResult<OrderSummaryDto>(items, page, pageSize, totalCount);
    }

    private static OrderSummaryDto MapToSummaryDto(OrderSummaryReadModel x) =>
        new(
            x.Id,
            x.UserId,
            x.Status,
            x.Total,
            x.Currency,
            x.ItemCount,
            x.PlacedAtUtc,
            x.UpdatedAtUtc);
}
