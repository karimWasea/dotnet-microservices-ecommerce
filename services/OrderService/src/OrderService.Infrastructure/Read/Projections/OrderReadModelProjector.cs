using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderService.Application.IntegrationEvents.Outgoing;
using OrderService.Application.Orders.DTOs;
using OrderService.Infrastructure.Read.Models;

namespace OrderService.Infrastructure.Read.Projections;

public sealed class OrderReadModelProjector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OrderReadDbContext _readDbContext;
    private readonly ILogger<OrderReadModelProjector> _logger;

    public OrderReadModelProjector(
        OrderReadDbContext readDbContext,
        ILogger<OrderReadModelProjector> logger)
    {
        _readDbContext = readDbContext;
        _logger = logger;
    }

    public async Task ProjectOrderPlacedAsync(OrderPlacedIntegrationEvent evt, CancellationToken ct = default)
    {
        var existingSummary = await _readDbContext.OrderSummaries
            .FirstOrDefaultAsync(x => x.Id == evt.OrderId, ct);

        if (existingSummary is not null)
        {
            _logger.LogInformation(
                "Order {OrderId} already exists in read database at Version={ExistingVersion}; skipping OrderPlaced (Version={IncomingVersion})",
                evt.OrderId,
                existingSummary.Version,
                evt.Version);
            return;
        }

        var itemCount = evt.Items.Sum(i => i.Quantity);

        var summary = new OrderSummaryReadModel
        {
            Id = evt.OrderId,
            UserId = evt.UserId,
            Status = evt.Status,
            Total = evt.Total,
            Currency = evt.Currency,
            ItemCount = itemCount,
            PlacedAtUtc = evt.PlacedAtUtc,
            UpdatedAtUtc = evt.PlacedAtUtc,
            Version = evt.Version
        };

        var itemsDto = evt.Items
            .Select(i => new OrderLineDto(
                i.ProductId,
                i.ProductName,
                i.UnitPrice,
                evt.Currency,
                i.Quantity,
                i.LineTotal))
            .ToList();

        var addressDto = new AddressDto(
            evt.ShippingAddress.RecipientName,
            evt.ShippingAddress.Street,
            evt.ShippingAddress.City,
            evt.ShippingAddress.Country);

        var history = new List<StatusHistoryEntryDto>
        {
            new(evt.Status, evt.PlacedAtUtc, null)
        };

        var details = new OrderDetailsReadModel
        {
            Id = evt.OrderId,
            UserId = evt.UserId,
            Status = evt.Status,
            Total = evt.Total,
            Currency = evt.Currency,
            ItemCount = itemCount,
            ShippingAddressJson = JsonSerializer.Serialize(addressDto, JsonOptions),
            ItemsJson = JsonSerializer.Serialize(itemsDto, JsonOptions),
            HistoryJson = JsonSerializer.Serialize(history, JsonOptions),
            PlacedAtUtc = evt.PlacedAtUtc,
            UpdatedAtUtc = evt.PlacedAtUtc,
            Version = evt.Version
        };

        _readDbContext.OrderSummaries.Add(summary);
        _readDbContext.OrderDetails.Add(details);
        await _readDbContext.SaveChangesAsync(ct);
    }

    public Task ProjectOrderStockReservedAsync(OrderStockReservedIntegrationEvent evt, CancellationToken ct = default)
    {
        return ApplyTransitionAsync(
            evt.OrderId,
            evt.Version,
            "StockReserved",
            evt.OccurredAtUtc,
            reason: null,
            cancelledBy: null,
            paymentId: null,
            ct);
    }

    public Task ProjectOrderConfirmedAsync(OrderConfirmedIntegrationEvent evt, CancellationToken ct = default)
    {
        return ApplyTransitionAsync(
            evt.OrderId,
            evt.Version,
            "Confirmed",
            evt.OccurredAtUtc,
            reason: null,
            cancelledBy: null,
            paymentId: evt.PaymentId,
            ct);
    }

    public Task ProjectOrderRejectedAsync(OrderRejectedIntegrationEvent evt, CancellationToken ct = default)
    {
        return ApplyTransitionAsync(
            evt.OrderId,
            evt.Version,
            "Rejected",
            evt.OccurredAtUtc,
            reason: evt.Reason,
            cancelledBy: null,
            paymentId: null,
            ct);
    }

    public Task ProjectOrderCancelledAsync(OrderCancelledIntegrationEvent evt, CancellationToken ct = default)
    {
        return ApplyTransitionAsync(
            evt.OrderId,
            evt.Version,
            "Cancelled",
            evt.OccurredAtUtc,
            reason: evt.Reason,
            cancelledBy: evt.CancelledBy,
            paymentId: null,
            ct);
    }

    private async Task ApplyTransitionAsync(
        Guid orderId,
        int incomingVersion,
        string newStatus,
        DateTimeOffset occurredAtUtc,
        string? reason,
        string? cancelledBy,
        string? paymentId,
        CancellationToken ct)
    {
        var summary = await _readDbContext.OrderSummaries
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        var details = await _readDbContext.OrderDetails
            .FirstOrDefaultAsync(x => x.Id == orderId, ct);

        if (summary is null || details is null)
        {
            throw new InvalidOperationException(
                $"Read model for OrderId '{orderId}' does not exist yet when projecting '{newStatus}' (Version={incomingVersion}). Will retry after OrderPlaced arrives.");
        }

        if (incomingVersion <= summary.Version)
        {
            _logger.LogInformation(
                "Skipping stale/duplicate projection for OrderId={OrderId}: IncomingVersion={IncomingVersion} <= CurrentVersion={CurrentVersion}",
                orderId,
                incomingVersion,
                summary.Version);
            return;
        }

        summary.Status = newStatus;
        summary.UpdatedAtUtc = occurredAtUtc;
        summary.Version = incomingVersion;

        details.Status = newStatus;
        if (reason is not null)
        {
            details.StatusReason = reason;
        }
        if (cancelledBy is not null)
        {
            details.CancelledBy = cancelledBy;
        }
        if (paymentId is not null)
        {
            details.PaymentId = paymentId;
        }
        details.UpdatedAtUtc = occurredAtUtc;
        details.Version = incomingVersion;

        var history = JsonSerializer.Deserialize<List<StatusHistoryEntryDto>>(details.HistoryJson, JsonOptions)
            ?? [];
        history.Add(new StatusHistoryEntryDto(newStatus, occurredAtUtc, reason));
        details.HistoryJson = JsonSerializer.Serialize(history, JsonOptions);

        await _readDbContext.SaveChangesAsync(ct);
    }
}
