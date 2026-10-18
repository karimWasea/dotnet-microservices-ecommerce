using UserService.Application.Abstractions;

namespace UserService.Application.Users.Commands.RevokeRefreshToken;

public sealed record RevokeRefreshTokenCommand(
    string RefreshToken) : ICommand;
