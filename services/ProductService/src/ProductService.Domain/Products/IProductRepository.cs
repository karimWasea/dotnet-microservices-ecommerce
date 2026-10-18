namespace ProductService.Domain.Products;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsBySkuAsync(Sku sku, CancellationToken cancellationToken = default);
    void Add(Product product);
}
