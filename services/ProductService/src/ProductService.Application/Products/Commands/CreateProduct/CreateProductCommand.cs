using ProductService.Application.Abstractions;

namespace ProductService.Application.Products.Commands.CreateProduct;

public sealed record CreateProductCommand(
    string Name,
    string? Description,
    string Sku,
    decimal Price,
    string Category) : ICommand<Guid>;
