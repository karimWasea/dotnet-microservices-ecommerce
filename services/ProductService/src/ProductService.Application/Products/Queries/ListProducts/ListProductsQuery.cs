using ProductService.Application.Abstractions;

namespace ProductService.Application.Products.Queries.ListProducts;

public sealed record ListProductsQuery(
    string? Search = null,
    string? Category = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string SortBy = "createdAt",
    string SortDir = "desc",
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResponse<ProductResponse>>;
