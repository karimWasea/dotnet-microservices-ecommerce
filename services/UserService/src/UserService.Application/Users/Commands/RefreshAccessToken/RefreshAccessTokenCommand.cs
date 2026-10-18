using UserService.Application.Abstractions;

namespace UserService.Application.Users.Commands.RefreshAccessToken;

public sealed record RefreshAccessTokenCommand(
    string RefreshToken) : ICommand<AuthTokensResponse>;
