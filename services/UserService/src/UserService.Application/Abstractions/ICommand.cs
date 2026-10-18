using MediatR;

namespace UserService.Application.Abstractions;

public interface ICommand : IRequest;

public interface ICommand<out TResponse> : IRequest<TResponse>;
