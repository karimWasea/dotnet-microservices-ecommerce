using Microsoft.EntityFrameworkCore;
using ProductService.Domain.Products;

namespace ProductService.Infrastructure.Persistence;

public sealed class ProductRepository(ProductDbContext dbContext) : IProductRepository
{
    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsBySkuAsync(Sku sku, CancellationToken cancellationToken = default)
    {
        var skuValue = sku.Value;
        return await dbContext.Products
            .AsNoTracking()
            .AnyAsync(p => p.Sku.Value == skuValue, cancellationToken);
    }

    public void Add(Product product)
    {
        dbContext.Products.Add(product);
    }
}
