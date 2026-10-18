using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UserService.Application.Abstractions;
using UserService.Domain.Users;

namespace UserService.Infrastructure.Security;

public sealed class JwtTokenService(
    IOptions<JwtOptions> jwtOptions,
    RsaKeyMaterial rsaKeyMaterial) : ITokenService
{
    private const int RefreshTokenByteLength = 32;
    private static readonly JsonWebTokenHandler TokenHandler = new();

    public AccessTokenResult CreateAccessToken(User user, DateTimeOffset utcNow)
    {
        var options = jwtOptions.Value;
        var accessMinutes = options.AccessTokenMinutes > 0 ? options.AccessTokenMinutes : 15;
        var expiresAtUtc = utcNow.AddMinutes(accessMinutes);
        var expiresInSeconds = (int)TimeSpan.FromMinutes(accessMinutes).TotalSeconds;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Email] = user.Email.Value,
            ["role"] = user.Role.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7(utcNow).ToString()
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Claims = claims,
            IssuedAt = utcNow.UtcDateTime,
            NotBefore = utcNow.UtcDateTime,
            Expires = expiresAtUtc.UtcDateTime,
            SigningCredentials = rsaKeyMaterial.SigningCredentials
        };

        var accessToken = TokenHandler.CreateToken(descriptor);

        return new AccessTokenResult(accessToken, expiresAtUtc, expiresInSeconds);
    }

    public RefreshTokenPair CreateRefreshToken(DateTimeOffset utcNow)
    {
        var options = jwtOptions.Value;
        var refreshDays = options.RefreshTokenDays > 0 ? options.RefreshTokenDays : 7;

        var randomBytes = RandomNumberGenerator.GetBytes(RefreshTokenByteLength);
        var rawToken = Base64UrlEncoder.Encode(randomBytes);
        var tokenHash = HashRefreshToken(rawToken);
        var expiresAtUtc = utcNow.AddDays(refreshDays);

        return new RefreshTokenPair(rawToken, tokenHash, expiresAtUtc);
    }

    public string HashRefreshToken(string rawRefreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var bytes = Encoding.UTF8.GetBytes(rawRefreshToken.Trim());
        var hashBytes = SHA256.HashData(bytes);

        return Convert.ToHexStringLower(hashBytes);
    }
}
