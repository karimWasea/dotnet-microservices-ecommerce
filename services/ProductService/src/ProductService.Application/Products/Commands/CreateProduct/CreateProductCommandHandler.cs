using ProductService.Application.Abstractions;
using ProductService.Application.Exceptions;
using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.Application.Products.Commands.CreateProduct;

public sealed class CreateProductCommandHandler(
    IProductRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<CreateProductCommand, Guid>
{
    public async Task<Guid> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        var sku = Sku.Create(request.Sku);
        var price = Price.Create(request.Price);

        if (await repository.ExistsBySkuAsync(sku, cancellationToken))
        {
            throw new ConflictException($"A product with SKU '{sku.Value}' already exists.");
        }

        var product = Product.Create(
            request.Name,
            request.Description,
            sku,
            price,
            request.Category,
            timeProvider.GetUtcNow());

        repository.Add(product);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return product.Id;
    }
}
