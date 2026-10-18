using UserService.Domain.Abstractions;
using UserService.Domain.Users.Events;

namespace UserService.Domain.Users;

public sealed class User : AggregateRoot
{
    public const int MaxDisplayNameLength = 100;
    public const int MaxFailedLoginAttempts = 5;
    public const int MaxActiveRefreshTokens = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan TokenRetentionWindow = TimeSpan.FromDays(7);

    private readonly List<RefreshToken> _refreshTokens = [];

    public Email Email { get; private set; } = null!;
    public string DisplayName { get; private set; } = string.Empty;
    public PasswordHash PasswordHash { get; private set; } = null!;
    public Role Role { get; private set; }
    public bool IsActive { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTimeOffset? LockedOutUntilUtc { get; private set; }
    public DateTimeOffset? LastLoginAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    // Required by EF Core.
    private User()
    {
    }

    private User(
        Guid id,
        Email email,
        string displayName,
        PasswordHash passwordHash,
        Role role,
        DateTimeOffset utcNow)
        : base(id)
    {
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Role = role;
        IsActive = true;
        FailedLoginCount = 0;
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public static User Register(
        Email email,
        string displayName,
        PasswordHash passwordHash,
        Role role,
        DateTimeOffset utcNow)
    {
        if (email is null)
        {
            throw new DomainException("Email is required.", "user.invalid_email");
        }

        if (passwordHash is null)
        {
            throw new DomainException("Password hash is required.", "user.invalid_password_hash");
        }

        var normalizedDisplayName = ValidateDisplayName(displayName);

        if (!Enum.IsDefined(role))
        {
            throw new DomainException("Role is invalid.", "user.invalid_role");
        }

        var user = new User(
            Guid.CreateVersion7(utcNow),
            email,
            normalizedDisplayName,
            passwordHash,
            role,
            utcNow);

        user.RaiseDomainEvent(new UserRegisteredDomainEvent(
            user.Id,
            user.Email.Value,
            user.Role.ToString(),
            utcNow));

        return user;
    }

    public bool CanLogin(DateTimeOffset utcNow)
    {
        if (!IsActive)
        {
            return false;
        }

        if (LockedOutUntilUtc.HasValue && LockedOutUntilUtc.Value > utcNow)
        {
            return false;
        }

        return true;
    }

    public void RecordFailedLogin(DateTimeOffset utcNow)
    {
        // A counter left over from an expired lock starts again from 0.
        if (LockedOutUntilUtc.HasValue && LockedOutUntilUtc.Value <= utcNow)
        {
            LockedOutUntilUtc = null;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        UpdatedAtUtc = utcNow;

        if (FailedLoginCount >= MaxFailedLoginAttempts)
        {
            var lockedUntil = utcNow.Add(LockoutDuration);
            LockedOutUntilUtc = lockedUntil;
            FailedLoginCount = 0;

            RaiseDomainEvent(new UserLockedOutDomainEvent(Id, lockedUntil, utcNow));
        }
    }

    public void RecordSuccessfulLogin(DateTimeOffset utcNow)
    {
        FailedLoginCount = 0;
        LockedOutUntilUtc = null;
        LastLoginAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public RefreshToken AddRefreshToken(
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new DomainException("Refresh token hash is required.", "user.invalid_token_hash");
        }

        if (expiresAtUtc <= utcNow)
        {
            throw new DomainException("Refresh token expiration must be in the future.", "user.invalid_token_expiry");
        }

        PruneOldTokens(utcNow);

        var activeTokens = _refreshTokens
            .Where(t => t.IsActive(utcNow))
            .OrderBy(t => t.CreatedAtUtc)
            .ToList();

        while (activeTokens.Count >= MaxActiveRefreshTokens)
        {
            var oldest = activeTokens[0];
            oldest.Revoke(utcNow);
            activeTokens.RemoveAt(0);
        }

        var refreshToken = new RefreshToken(
            Guid.CreateVersion7(utcNow),
            Id,
            tokenHash.Trim(),
            utcNow,
            expiresAtUtc);

        _refreshTokens.Add(refreshToken);
        UpdatedAtUtc = utcNow;

        return refreshToken;
    }

    /// <summary>
    /// Rotates a refresh token and returns a <see cref="RefreshOutcome"/> instead of throwing,
    /// so that when token theft is suspected (<see cref="RefreshOutcome.ReuseDetected"/>),
    /// the caller can commit the revocation of all tokens before returning 401.
    /// </summary>
    public RefreshOutcome RotateRefreshToken(
        string presentedHash,
        string newHash,
        DateTimeOffset newExpiresAtUtc,
        DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(presentedHash))
        {
            return RefreshOutcome.Invalid;
        }

        var token = _refreshTokens.FirstOrDefault(t => t.TokenHash == presentedHash);

        // 1. Token not found -> Invalid (no change)
        if (token is null)
        {
            return RefreshOutcome.Invalid;
        }

        // 2. Token was already rotated before (ReplacedByTokenHash is set) -> token theft suspected
        if (token.ReplacedByTokenHash is not null)
        {
            RevokeAllRefreshTokens(utcNow);
            UpdatedAtUtc = utcNow;
            RaiseDomainEvent(new RefreshTokenReuseDetectedDomainEvent(Id, utcNow));
            return RefreshOutcome.ReuseDetected;
        }

        // 3. Token is revoked, or the user is inactive -> Invalid (no change)
        if (token.IsRevoked || !IsActive)
        {
            return RefreshOutcome.Invalid;
        }

        // 4. Token is expired -> Expired (no change)
        if (token.IsExpired(utcNow))
        {
            return RefreshOutcome.Expired;
        }

        // 5. Otherwise: mark the presented token revoked and replaced, add the new token, return Rotated
        token.Revoke(utcNow, newHash.Trim());
        AddRefreshToken(newHash, newExpiresAtUtc, utcNow);
        UpdatedAtUtc = utcNow;

        return RefreshOutcome.Rotated;
    }

    public void RevokeRefreshToken(string tokenHash, DateTimeOffset utcNow)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            return;
        }

        var token = _refreshTokens.FirstOrDefault(t => t.TokenHash == tokenHash);
        if (token is null || token.IsRevoked)
        {
            return;
        }

        token.Revoke(utcNow);
        UpdatedAtUtc = utcNow;
    }

    public void ChangePassword(PasswordHash newPasswordHash, DateTimeOffset utcNow)
    {
        if (!IsActive)
        {
            throw new DomainException("An inactive user cannot change the password.", "user.inactive");
        }

        if (newPasswordHash is null)
        {
            throw new DomainException("Password hash is required.", "user.invalid_password_hash");
        }

        PasswordHash = newPasswordHash;
        RevokeAllRefreshTokens(utcNow);
        UpdatedAtUtc = utcNow;

        RaiseDomainEvent(new PasswordChangedDomainEvent(Id, utcNow));
    }

    public void Deactivate(DateTimeOffset utcNow)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        RevokeAllRefreshTokens(utcNow);
        UpdatedAtUtc = utcNow;

        RaiseDomainEvent(new UserDeactivatedDomainEvent(Id, utcNow));
    }

    public void Activate(DateTimeOffset utcNow)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        FailedLoginCount = 0;
        LockedOutUntilUtc = null;
        UpdatedAtUtc = utcNow;

        RaiseDomainEvent(new UserActivatedDomainEvent(Id, utcNow));
    }

    private void RevokeAllRefreshTokens(DateTimeOffset utcNow)
    {
        foreach (var token in _refreshTokens)
        {
            if (!token.IsRevoked)
            {
                token.Revoke(utcNow);
            }
        }
    }

    private void PruneOldTokens(DateTimeOffset utcNow)
    {
        var cutoff = utcNow.Subtract(TokenRetentionWindow);

        _refreshTokens.RemoveAll(t =>
            t.ExpiresAtUtc <= cutoff ||
            (t.RevokedAtUtc.HasValue && t.RevokedAtUtc.Value <= cutoff));
    }

    private static string ValidateDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new DomainException("Display name is required.", "user.invalid_display_name");
        }

        var trimmed = displayName.Trim();
        if (trimmed.Length > MaxDisplayNameLength)
        {
            throw new DomainException($"Display name must not exceed {MaxDisplayNameLength} characters.", "user.invalid_display_name");
        }

        return trimmed;
    }
}
