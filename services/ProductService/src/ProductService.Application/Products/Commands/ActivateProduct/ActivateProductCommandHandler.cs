using ProductService.Application.Abstractions;
using ProductService.Application.Exceptions;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.Application.Products.Commands.ActivateProduct;

public sealed class ActivateProductCommandHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<ActivateProductCommand>
{
    public async Task Handle(ActivateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"Product with ID '{request.Id}' was not found.");

        product.Activate(timeProvider.GetUtcNow());

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
