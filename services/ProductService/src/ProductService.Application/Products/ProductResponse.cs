namespace ProductService.Application.Products;

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    string Category,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    uint Version = 0);
