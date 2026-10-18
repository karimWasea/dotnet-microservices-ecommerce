using ProductService.Application.Abstractions;

namespace ProductService.Application.Products.Queries.GetProductById;

public sealed record GetProductByIdQuery(Guid Id) : IQuery<ProductResponse>;
