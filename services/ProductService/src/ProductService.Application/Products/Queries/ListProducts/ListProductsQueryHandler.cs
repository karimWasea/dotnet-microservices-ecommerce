using ProductService.Application.Abstractions;

namespace ProductService.Application.Products.Queries.ListProducts;

public sealed class ListProductsQueryHandler(
    IProductReadService readService) : IQueryHandler<ListProductsQuery, PagedResponse<ProductResponse>>
{
    public async Task<PagedResponse<ProductResponse>> Handle(
        ListProductsQuery request,
        CancellationToken cancellationToken)
    {
        return await readService.ListActiveAsync(request, cancellationToken);
    }
}
