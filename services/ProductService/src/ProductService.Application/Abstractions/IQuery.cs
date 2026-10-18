using MediatR;

namespace ProductService.Application.Abstractions;

public interface IQuery<out TResponse> : IRequest<TResponse>;
