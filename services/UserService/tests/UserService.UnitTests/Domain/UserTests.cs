using UserService.Domain.Abstractions;
using UserService.Domain.Users;
using UserService.Domain.Users.Events;

namespace UserService.UnitTests.Domain;

public sealed class UserTests
{
    private static readonly DateTimeOffset BaseUtcNow = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Email DefaultEmail = Email.Create("customer@ecommerce.local");
    private static readonly PasswordHash DefaultPasswordHash = PasswordHash.Create("AQAAAAIAAYagAAAAEValidHashedPasswordString1234567890==");

    private static User CreateDefaultUser(DateTimeOffset? utcNow = null) =>
        User.Register(
            DefaultEmail,
            "  Alice Customer  ",
            DefaultPasswordHash,
            Role.Customer,
            utcNow ?? BaseUtcNow);

    [Fact]
    public void Register_WithValidArguments_InitializesUserAndRaisesEvent()
    {
        var user = CreateDefaultUser();

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("customer@ecommerce.local", user.Email.Value);
        Assert.Equal("Alice Customer", user.DisplayName);
        Assert.Equal(DefaultPasswordHash, user.PasswordHash);
        Assert.Equal(Role.Customer, user.Role);
        Assert.True(user.IsActive);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockedOutUntilUtc);
        Assert.Null(user.LastLoginAtUtc);
        Assert.Equal(BaseUtcNow, user.CreatedAtUtc);
        Assert.Equal(BaseUtcNow, user.UpdatedAtUtc);

        var evt = Assert.Single(user.DomainEvents);
        var registered = Assert.IsType<UserRegisteredDomainEvent>(evt);
        Assert.Equal(user.Id, registered.UserId);
        Assert.Equal("customer@ecommerce.local", registered.Email);
        Assert.Equal("Customer", registered.Role);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_WithBlankDisplayName_ThrowsDomainException(string displayName)
    {
        var ex = Assert.Throws<DomainException>(() =>
            User.Register(DefaultEmail, displayName, DefaultPasswordHash, Role.Customer, BaseUtcNow));

        Assert.Equal("user.invalid_display_name", ex.Code);
    }

    [Fact]
    public void Register_WithDisplayNameExceeding100Chars_ThrowsDomainException()
    {
        var longName = new string('x', 101);

        var ex = Assert.Throws<DomainException>(() =>
            User.Register(DefaultEmail, longName, DefaultPasswordHash, Role.Customer, BaseUtcNow));

        Assert.Equal("user.invalid_display_name", ex.Code);
    }

    [Fact]
    public void RecordFailedLogin_AtFifthAttempt_LocksUserFor15MinutesAndResetsCounter()
    {
        var user = CreateDefaultUser();
        user.ClearDomainEvents();

        for (var i = 1; i <= 4; i++)
        {
            user.RecordFailedLogin(BaseUtcNow.AddMinutes(i));
            Assert.Equal(i, user.FailedLoginCount);
            Assert.Null(user.LockedOutUntilUtc);
            Assert.True(user.CanLogin(BaseUtcNow.AddMinutes(i)));
            Assert.Empty(user.DomainEvents);
        }

        var fifthAttemptTime = BaseUtcNow.AddMinutes(5);
        user.RecordFailedLogin(fifthAttemptTime);

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Equal(fifthAttemptTime.AddMinutes(15), user.LockedOutUntilUtc);
        Assert.False(user.CanLogin(fifthAttemptTime.AddMinutes(10)));
        Assert.True(user.CanLogin(fifthAttemptTime.AddMinutes(15)));

        var evt = Assert.Single(user.DomainEvents);
        var lockedOut = Assert.IsType<UserLockedOutDomainEvent>(evt);
        Assert.Equal(user.Id, lockedOut.UserId);
        Assert.Equal(fifthAttemptTime.AddMinutes(15), lockedOut.LockedOutUntilUtc);
    }

    [Fact]
    public void RecordFailedLogin_AfterLockoutExpired_StartsCounterAgainFromZero()
    {
        var user = CreateDefaultUser();
        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedLogin(BaseUtcNow);
        }

        var afterLockout = BaseUtcNow.AddMinutes(16);
        user.RecordFailedLogin(afterLockout);

        Assert.Equal(1, user.FailedLoginCount);
        Assert.Null(user.LockedOutUntilUtc);
        Assert.True(user.CanLogin(afterLockout));
    }

    [Fact]
    public void RecordSuccessfulLogin_ClearsFailedCountAndLockoutAndSetsLastLoginAt()
    {
        var user = CreateDefaultUser();
        user.RecordFailedLogin(BaseUtcNow);
        user.RecordFailedLogin(BaseUtcNow.AddMinutes(1));

        var loginTime = BaseUtcNow.AddMinutes(2);
        user.RecordSuccessfulLogin(loginTime);

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockedOutUntilUtc);
        Assert.Equal(loginTime, user.LastLoginAtUtc);
    }

    [Fact]
    public void AddRefreshToken_WhenSixthActiveTokenAdded_RevokesOldestActiveToken()
    {
        var user = CreateDefaultUser();

        for (var i = 1; i <= 6; i++)
        {
            var now = BaseUtcNow.AddMinutes(i);
            user.AddRefreshToken($"hash-{i}", now.AddDays(7), now);
        }

        var checkTime = BaseUtcNow.AddMinutes(10);
        var activeTokens = user.RefreshTokens.Where(t => t.IsActive(checkTime)).ToList();

        Assert.Equal(5, activeTokens.Count);
        var firstToken = user.RefreshTokens.Single(t => t.TokenHash == "hash-1");
        Assert.True(firstToken.IsRevoked);
        Assert.DoesNotContain(activeTokens, t => t.TokenHash == "hash-1");
    }

    [Fact]
    public void AddRefreshToken_PrunesTokensExpiredOrRevokedMoreThan7DaysAgo()
    {
        var user = CreateDefaultUser();

        // Add token 1 and revoke it at BaseUtcNow
        user.AddRefreshToken("old-revoked-hash", BaseUtcNow.AddDays(7), BaseUtcNow);
        user.RevokeRefreshToken("old-revoked-hash", BaseUtcNow);

        // Add token 2 that expires at BaseUtcNow + 1 day
        user.AddRefreshToken("old-expired-hash", BaseUtcNow.AddDays(1), BaseUtcNow);

        // Move clock forward 9 days so both tokens are > 7 days past revocation/expiration
        var futureNow = BaseUtcNow.AddDays(9);
        user.AddRefreshToken("fresh-hash", futureNow.AddDays(7), futureNow);

        Assert.Single(user.RefreshTokens);
        Assert.Equal("fresh-hash", user.RefreshTokens.Single().TokenHash);
    }

    [Fact]
    public void RotateRefreshToken_WithValidActiveToken_MarksOldReplacedAndAddsNew()
    {
        var user = CreateDefaultUser();
        user.AddRefreshToken("token-hash-1", BaseUtcNow.AddDays(7), BaseUtcNow);

        var rotateTime = BaseUtcNow.AddHours(1);
        var outcome = user.RotateRefreshToken(
            "token-hash-1",
            "token-hash-2",
            rotateTime.AddDays(7),
            rotateTime);

        Assert.Equal(RefreshOutcome.Rotated, outcome);

        var oldToken = user.RefreshTokens.Single(t => t.TokenHash == "token-hash-1");
        var newToken = user.RefreshTokens.Single(t => t.TokenHash == "token-hash-2");

        Assert.True(oldToken.IsRevoked);
        Assert.Equal(rotateTime, oldToken.RevokedAtUtc);
        Assert.Equal("token-hash-2", oldToken.ReplacedByTokenHash);
        Assert.True(newToken.IsActive(rotateTime));
    }

    [Fact]
    public void RotateRefreshToken_WhenAlreadyRotatedTokenReused_RevokesAllTokensAndRaisesReuseEvent()
    {
        var user = CreateDefaultUser();
        user.AddRefreshToken("token-hash-1", BaseUtcNow.AddDays(7), BaseUtcNow);
        user.AddRefreshToken("other-device-hash", BaseUtcNow.AddDays(7), BaseUtcNow.AddMinutes(1));

        // Legitimate rotation of token-hash-1 -> token-hash-2
        var firstRotateTime = BaseUtcNow.AddHours(1);
        var firstOutcome = user.RotateRefreshToken(
            "token-hash-1",
            "token-hash-2",
            firstRotateTime.AddDays(7),
            firstRotateTime);
        Assert.Equal(RefreshOutcome.Rotated, firstOutcome);

        user.ClearDomainEvents();

        // Attacker or replay presents already-rotated token-hash-1 again!
        var replayTime = BaseUtcNow.AddHours(2);
        var reuseOutcome = user.RotateRefreshToken(
            "token-hash-1",
            "token-hash-3",
            replayTime.AddDays(7),
            replayTime);

        Assert.Equal(RefreshOutcome.ReuseDetected, reuseOutcome);
        Assert.All(user.RefreshTokens, t => Assert.True(t.IsRevoked));
        Assert.DoesNotContain(user.RefreshTokens, t => t.TokenHash == "token-hash-3");

        var evt = Assert.Single(user.DomainEvents);
        var reuseEvent = Assert.IsType<RefreshTokenReuseDetectedDomainEvent>(evt);
        Assert.Equal(user.Id, reuseEvent.UserId);
    }

    [Fact]
    public void RotateRefreshToken_WithUnknownOrRevokedOrExpiredToken_ReturnsExpectedOutcome()
    {
        var user = CreateDefaultUser();
        user.AddRefreshToken("revoked-hash", BaseUtcNow.AddDays(7), BaseUtcNow);
        user.RevokeRefreshToken("revoked-hash", BaseUtcNow.AddMinutes(5));

        user.AddRefreshToken("expired-hash", BaseUtcNow.AddHours(1), BaseUtcNow);

        var checkTime = BaseUtcNow.AddHours(2);

        Assert.Equal(
            RefreshOutcome.Invalid,
            user.RotateRefreshToken("unknown-hash", "new-1", checkTime.AddDays(7), checkTime));

        Assert.Equal(
            RefreshOutcome.Invalid,
            user.RotateRefreshToken("revoked-hash", "new-2", checkTime.AddDays(7), checkTime));

        Assert.Equal(
            RefreshOutcome.Expired,
            user.RotateRefreshToken("expired-hash", "new-3", checkTime.AddDays(7), checkTime));
    }

    [Fact]
    public void RevokeRefreshToken_IsIdempotentAndIgnoresUnknownToken()
    {
        var user = CreateDefaultUser();
        user.AddRefreshToken("token-hash-1", BaseUtcNow.AddDays(7), BaseUtcNow);

        var revokeTime = BaseUtcNow.AddMinutes(10);
        user.RevokeRefreshToken("token-hash-1", revokeTime);
        user.RevokeRefreshToken("token-hash-1", revokeTime.AddMinutes(5));
        user.RevokeRefreshToken("non-existent-hash", revokeTime);

        var token = Assert.Single(user.RefreshTokens);
        Assert.True(token.IsRevoked);
        Assert.Equal(revokeTime, token.RevokedAtUtc);
    }

    [Fact]
    public void ChangePassword_WhenActive_UpdatesHashRevokesAllTokensAndRaisesEvent()
    {
        var user = CreateDefaultUser();
        user.AddRefreshToken("token-1", BaseUtcNow.AddDays(7), BaseUtcNow);
        user.AddRefreshToken("token-2", BaseUtcNow.AddDays(7), BaseUtcNow.AddMinutes(1));
        user.ClearDomainEvents();

        var newHash = PasswordHash.Create("AQAAAAIAAYagAAAAENewPasswordHashStringValue9876543210==");
        var changeTime = BaseUtcNow.AddHours(1);

        user.ChangePassword(newHash, changeTime);

        Assert.Equal(newHash, user.PasswordHash);
        Assert.All(user.RefreshTokens, t => Assert.True(t.IsRevoked));

        var evt = Assert.Single(user.DomainEvents);
        Assert.IsType<PasswordChangedDomainEvent>(evt);
    }

    [Fact]
    public void ChangePassword_WhenInactive_ThrowsDomainException()
    {
        var user = CreateDefaultUser();
        user.Deactivate(BaseUtcNow.AddMinutes(5));

        var newHash = PasswordHash.Create("AQAAAAIAAYagAAAAENewPasswordHashStringValue9876543210==");

        var ex = Assert.Throws<DomainException>(() =>
            user.ChangePassword(newHash, BaseUtcNow.AddMinutes(10)));

        Assert.Equal("user.inactive", ex.Code);
    }

    [Fact]
    public void DeactivateAndActivate_AreIdempotentAndManageTokensAndLockout()
    {
        var user = CreateDefaultUser();
        user.AddRefreshToken("token-1", BaseUtcNow.AddDays(7), BaseUtcNow);
        for (var i = 0; i < 5; i++)
        {
            user.RecordFailedLogin(BaseUtcNow.AddMinutes(1));
        }
        user.ClearDomainEvents();

        // Deactivate
        var deactivateTime = BaseUtcNow.AddMinutes(5);
        user.Deactivate(deactivateTime);
        user.Deactivate(deactivateTime.AddMinutes(1)); // second call is no-op

        Assert.False(user.IsActive);
        Assert.False(user.CanLogin(deactivateTime));
        Assert.All(user.RefreshTokens, t => Assert.True(t.IsRevoked));
        Assert.Single(user.DomainEvents);
        Assert.IsType<UserDeactivatedDomainEvent>(user.DomainEvents[0]);

        user.ClearDomainEvents();

        // Activate
        var activateTime = BaseUtcNow.AddMinutes(6);
        user.Activate(activateTime);
        user.Activate(activateTime.AddMinutes(1)); // second call is no-op

        Assert.True(user.IsActive);
        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockedOutUntilUtc);
        Assert.True(user.CanLogin(activateTime));
        Assert.Single(user.DomainEvents);
        Assert.IsType<UserActivatedDomainEvent>(user.DomainEvents[0]);
    }
}
