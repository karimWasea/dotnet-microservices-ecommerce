namespace OrderService.Domain.Orders;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    void Add(Order order);
    Task<IReadOnlyList<Guid>> GetStaleOrderIdsAsync(
        DateTimeOffset cutoffUtc,
        int limit,
        CancellationToken cancellationToken = default);
}
