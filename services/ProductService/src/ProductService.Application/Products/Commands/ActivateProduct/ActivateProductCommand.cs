using ProductService.Application.Abstractions;

namespace ProductService.Application.Products.Commands.ActivateProduct;

public sealed record ActivateProductCommand(Guid Id) : ICommand;
