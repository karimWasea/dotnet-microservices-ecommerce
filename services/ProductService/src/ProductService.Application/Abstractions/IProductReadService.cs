using ProductService.Application.Products;
using ProductService.Application.Products.Queries.ListProducts;

namespace ProductService.Application.Abstractions;

public interface IProductReadService
{
    Task<ProductResponse?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResponse<ProductResponse>> ListActiveAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken = default);
}
