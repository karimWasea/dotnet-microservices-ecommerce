using UserService.Domain.Abstractions;

namespace UserService.Domain.Users;

/// <summary>
/// Child entity owned exclusively by the <see cref="User"/> aggregate root.
/// Cannot be created or mutated outside of <see cref="User"/> methods.
/// </summary>
public sealed class RefreshToken : Entity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public bool IsRevoked => RevokedAtUtc.HasValue;

    public bool IsExpired(DateTimeOffset utcNow) => utcNow >= ExpiresAtUtc;

    public bool IsActive(DateTimeOffset utcNow) => !IsRevoked && !IsExpired(utcNow);

    // Required by EF Core.
    private RefreshToken()
    {
    }

    internal RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    internal void Revoke(DateTimeOffset utcNow, string? replacedByTokenHash = null)
    {
        if (!RevokedAtUtc.HasValue)
        {
            RevokedAtUtc = utcNow;
        }

        if (replacedByTokenHash is not null)
        {
            ReplacedByTokenHash = replacedByTokenHash;
        }
    }
}
