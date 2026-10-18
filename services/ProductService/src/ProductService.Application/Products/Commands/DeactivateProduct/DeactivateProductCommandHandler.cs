using ProductService.Application.Abstractions;
using ProductService.Application.Exceptions;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.Application.Products.Commands.DeactivateProduct;

public sealed class DeactivateProductCommandHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<DeactivateProductCommand>
{
    public async Task Handle(DeactivateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"Product with ID '{request.Id}' was not found.");

        product.Deactivate(timeProvider.GetUtcNow());

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
