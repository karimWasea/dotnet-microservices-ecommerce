using OrderService.Application.Orders.DTOs;

namespace OrderService.Application.Abstractions;

public interface IOrderReadService
{
    Task<OrderDetailsDto?> GetByIdAsync(Guid orderId, CancellationToken ct = default);

    Task<PagedResult<OrderSummaryDto>> ListByUserAsync(
        Guid userId,
        int page,
        int pageSize,
        string? status = null,
        CancellationToken ct = default);

    Task<PagedResult<OrderSummaryDto>> ListAllAsync(
        int page,
        int pageSize,
        Guid? userId = null,
        string? status = null,
        CancellationToken ct = default);
}
