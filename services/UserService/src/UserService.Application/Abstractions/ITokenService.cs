using UserService.Domain.Users;

namespace UserService.Application.Abstractions;

public sealed record AccessTokenResult(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    int ExpiresInSeconds);

public sealed record RefreshTokenPair(
    string RawToken,
    string TokenHash,
    DateTimeOffset ExpiresAtUtc);

public interface ITokenService
{
    AccessTokenResult CreateAccessToken(User user, DateTimeOffset utcNow);
    RefreshTokenPair CreateRefreshToken(DateTimeOffset utcNow);
    string HashRefreshToken(string rawRefreshToken);
}
