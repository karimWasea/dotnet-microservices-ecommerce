namespace UserService.Application.Users;

public sealed record AuthTokensResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    UserResponse User);
