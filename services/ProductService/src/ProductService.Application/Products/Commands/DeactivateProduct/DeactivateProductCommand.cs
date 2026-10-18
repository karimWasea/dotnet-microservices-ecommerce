using ProductService.Application.Abstractions;

namespace ProductService.Application.Products.Commands.DeactivateProduct;

public sealed record DeactivateProductCommand(Guid Id) : ICommand;
