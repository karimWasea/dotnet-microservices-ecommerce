using UserService.Application.Abstractions;

namespace UserService.Application.Users.Commands.Login;

public sealed record LoginCommand(
    string Email,
    string Password) : ICommand<AuthTokensResponse>;
