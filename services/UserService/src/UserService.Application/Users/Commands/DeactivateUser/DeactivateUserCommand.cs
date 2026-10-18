using UserService.Application.Abstractions;

namespace UserService.Application.Users.Commands.DeactivateUser;

public sealed record DeactivateUserCommand(Guid UserId) : ICommand;
