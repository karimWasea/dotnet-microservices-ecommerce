using Microsoft.EntityFrameworkCore;
using ProductService.Application.Abstractions;
using ProductService.Application.Products;
using ProductService.Application.Products.Queries.ListProducts;
using ProductService.Domain.Products;

namespace ProductService.Infrastructure.Persistence;

public sealed class ProductReadService(ProductDbContext dbContext) : IProductReadService
{
    public async Task<ProductResponse?> GetActiveByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Products
            .AsNoTracking()
            .Where(p => p.Id == id && p.IsActive)
            .Select(p => new ProductResponse(
                p.Id,
                p.Name,
                p.Description,
                p.Sku.Value,
                p.Price.Amount,
                p.Category,
                p.CreatedAtUtc,
                p.UpdatedAtUtc,
                p.Version))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PagedResponse<ProductResponse>> ListActiveAsync(
        ListProductsQuery query,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Product> productsQuery = dbContext.Products
            .AsNoTracking()
            .Where(p => p.IsActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var searchLower = query.Search.Trim().ToLowerInvariant();
            productsQuery = productsQuery.Where(p =>
                p.Name.ToLower().Contains(searchLower) ||
                (p.Description != null && p.Description.ToLower().Contains(searchLower)));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var categoryLower = query.Category.Trim().ToLowerInvariant();
            productsQuery = productsQuery.Where(p => p.Category.ToLower() == categoryLower);
        }

        if (query.MinPrice.HasValue)
        {
            var minPrice = query.MinPrice.Value;
            productsQuery = productsQuery.Where(p => p.Price.Amount >= minPrice);
        }

        if (query.MaxPrice.HasValue)
        {
            var maxPrice = query.MaxPrice.Value;
            productsQuery = productsQuery.Where(p => p.Price.Amount <= maxPrice);
        }

        var totalCount = await productsQuery.CountAsync(cancellationToken);

        var isAscending = string.Equals(query.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        var sortBy = query.SortBy.Trim().ToLowerInvariant();

        productsQuery = (sortBy, isAscending) switch
        {
            ("name", true) => productsQuery.OrderBy(p => p.Name).ThenBy(p => p.Id),
            ("name", false) => productsQuery.OrderByDescending(p => p.Name).ThenBy(p => p.Id),
            ("price", true) => productsQuery.OrderBy(p => p.Price.Amount).ThenBy(p => p.Id),
            ("price", false) => productsQuery.OrderByDescending(p => p.Price.Amount).ThenBy(p => p.Id),
            (_, true) => productsQuery.OrderBy(p => p.CreatedAtUtc).ThenBy(p => p.Id),
            _ => productsQuery.OrderByDescending(p => p.CreatedAtUtc).ThenBy(p => p.Id)
        };

        var skip = (query.Page - 1) * query.PageSize;

        var items = await productsQuery
            .Skip(skip)
            .Take(query.PageSize)
            .Select(p => new ProductResponse(
                p.Id,
                p.Name,
                p.Description,
                p.Sku.Value,
                p.Price.Amount,
                p.Category,
                p.CreatedAtUtc,
                p.UpdatedAtUtc,
                p.Version))
            .ToListAsync(cancellationToken);

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return new PagedResponse<ProductResponse>(
            items,
            query.Page,
            query.PageSize,
            totalCount,
            totalPages);
    }
}
