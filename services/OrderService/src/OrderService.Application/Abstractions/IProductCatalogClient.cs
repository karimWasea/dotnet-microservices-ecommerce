namespace OrderService.Application.Abstractions;

public sealed record ProductSnapshot(
    Guid Id,
    string Name,
    decimal Price,
    string Currency,
    bool IsActive);

public interface IProductCatalogClient
{
    Task<ProductSnapshot?> GetProductAsync(Guid productId, CancellationToken ct = default);
}
