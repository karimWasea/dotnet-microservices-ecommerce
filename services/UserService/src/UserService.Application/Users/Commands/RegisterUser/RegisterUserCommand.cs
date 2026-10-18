using UserService.Application.Abstractions;

namespace UserService.Application.Users.Commands.RegisterUser;

public sealed record RegisterUserCommand(
    string Email,
    string Password,
    string DisplayName) : ICommand<UserResponse>;
