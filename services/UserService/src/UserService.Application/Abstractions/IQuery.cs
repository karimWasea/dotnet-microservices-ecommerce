using MediatR;

namespace UserService.Application.Abstractions;

public interface IQuery<out TResponse> : IRequest<TResponse>;
