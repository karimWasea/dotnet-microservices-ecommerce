using ProductService.Application.Abstractions;

namespace ProductService.Application.Products.Commands.UpdateProduct;

public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    string Category,
    uint? Version = null) : ICommand;
