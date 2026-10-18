using ProductService.Application.Abstractions;
using ProductService.Application.Exceptions;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.Application.Products.Commands.UpdateProduct;

public sealed class UpdateProductCommandHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<UpdateProductCommand>
{
    public async Task Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"Product with ID '{request.Id}' was not found.");

        if (request.Version.HasValue && product.Version != request.Version.Value)
        {
            throw new ConflictException("The product was modified by another request. Please reload and try again.");
        }

        var utcNow = timeProvider.GetUtcNow();
        var newPrice = Price.Create(request.Price);

        product.Update(request.Name, request.Description, request.Category, newPrice, utcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
