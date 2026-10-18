using UserService.Application.Abstractions;

namespace UserService.Application.Users.Commands.ActivateUser;

public sealed record ActivateUserCommand(Guid UserId) : ICommand;
