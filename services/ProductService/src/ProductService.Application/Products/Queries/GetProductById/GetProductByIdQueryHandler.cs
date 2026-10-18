using ProductService.Application.Abstractions;
using ProductService.Application.Exceptions;

namespace ProductService.Application.Products.Queries.GetProductById;

public sealed class GetProductByIdQueryHandler(
    IProductReadService readService) : IQueryHandler<GetProductByIdQuery, ProductResponse>
{
    public async Task<ProductResponse> Handle(
        GetProductByIdQuery request,
        CancellationToken cancellationToken)
    {
        var product = await readService.GetActiveByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException($"Product with ID '{request.Id}' was not found.");

        return product;
    }
}
