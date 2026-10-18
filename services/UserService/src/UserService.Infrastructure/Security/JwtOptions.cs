namespace UserService.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "ecommerce.userservice";
    public string Audience { get; set; } = "ecommerce.api";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    public string KeyId { get; set; } = "userservice-dev-key-1";
    public string? PrivateKeyPem { get; set; }
    public string PrivateKeyPath { get; set; } = ".keys/userservice-dev-rsa.pem";
}
