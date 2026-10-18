using NSubstitute;
using UserService.Application.Abstractions;
using UserService.Application.Exceptions;
using UserService.Application.Users.Commands.ActivateUser;
using UserService.Application.Users.Commands.ChangePassword;
using UserService.Application.Users.Commands.DeactivateUser;
using UserService.Application.Users.Commands.Login;
using UserService.Application.Users.Commands.RefreshAccessToken;
using UserService.Application.Users.Commands.RegisterUser;
using UserService.Application.Users.Commands.RevokeRefreshToken;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.UnitTests.Application;

public sealed class CommandHandlersTests
{
    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static readonly DateTimeOffset FixedNow = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
    private const string ValidHashedPassword = "AQAAAAIAAYagAAAAEValidHashedPasswordString1234567890==";

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITokenService _tokenService = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    [Fact]
    public async Task RegisterUser_WhenEmailIsUnique_CreatesCustomerAndSaves()
    {
        var command = new RegisterUserCommand("Alice@Example.com", "Customer@123456!", "Alice Smith");
        _userRepository.ExistsByEmailAsync(Email.Create("alice@example.com"), Arg.Any<CancellationToken>())
            .Returns(false);
        _passwordHasher.Hash(command.Password).Returns(ValidHashedPassword);

        var handler = new RegisterUserCommandHandler(
            _userRepository,
            _passwordHasher,
            _unitOfWork,
            _timeProvider);

        var response = await handler.Handle(command, CancellationToken.None);

        Assert.Equal("alice@example.com", response.Email);
        Assert.Equal("Alice Smith", response.DisplayName);
        Assert.Equal("Customer", response.Role);
        Assert.True(response.IsActive);

        _userRepository.Received(1).Add(Arg.Is<User>(u =>
            u.Email.Value == "alice@example.com" &&
            u.Role == Role.Customer));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterUser_WhenEmailAlreadyExists_ThrowsConflictExceptionWithoutSaving()
    {
        var command = new RegisterUserCommand("alice@example.com", "Customer@123456!", "Alice Smith");
        _userRepository.ExistsByEmailAsync(Email.Create("alice@example.com"), Arg.Any<CancellationToken>())
            .Returns(true);

        var handler = new RegisterUserCommandHandler(
            _userRepository,
            _passwordHasher,
            _unitOfWork,
            _timeProvider);

        await Assert.ThrowsAsync<ConflictException>(() =>
            handler.Handle(command, CancellationToken.None));

        _userRepository.DidNotReceive().Add(Arg.Any<User>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WithValidCredentials_IssuesTokensAndSaves()
    {
        var user = User.Register(
            Email.Create("alice@example.com"),
            "Alice",
            PasswordHash.Create(ValidHashedPassword),
            Role.Customer,
            FixedNow.AddHours(-1));

        _userRepository.GetByEmailAsync(Email.Create("alice@example.com"), Arg.Any<CancellationToken>())
            .Returns(user);
        _passwordHasher.Verify("Customer@123456!", user.PasswordHash).Returns(true);
        _tokenService.CreateRefreshToken(FixedNow)
            .Returns(new RefreshTokenPair("raw-refresh-token", "hashed-refresh-token", FixedNow.AddDays(7)));
        _tokenService.CreateAccessToken(user, FixedNow)
            .Returns(new AccessTokenResult("jwt-access-token", FixedNow.AddMinutes(15), 900));

        var handler = new LoginCommandHandler(
            _userRepository,
            _passwordHasher,
            _tokenService,
            _unitOfWork,
            _timeProvider);

        var result = await handler.Handle(
            new LoginCommand("alice@example.com", "Customer@123456!"),
            CancellationToken.None);

        Assert.Equal("jwt-access-token", result.AccessToken);
        Assert.Equal("Bearer", result.TokenType);
        Assert.Equal(900, result.ExpiresInSeconds);
        Assert.Equal("raw-refresh-token", result.RefreshToken);
        Assert.Equal(FixedNow, user.LastLoginAtUtc);
        Assert.Single(user.RefreshTokens);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WithWrongPassword_SavesFailedLoginCountBeforeThrowingUnauthorizedException()
    {
        var user = User.Register(
            Email.Create("alice@example.com"),
            "Alice",
            PasswordHash.Create(ValidHashedPassword),
            Role.Customer,
            FixedNow.AddHours(-1));

        _userRepository.GetByEmailAsync(Email.Create("alice@example.com"), Arg.Any<CancellationToken>())
            .Returns(user);
        _passwordHasher.Verify("WrongPassword!", user.PasswordHash).Returns(false);

        var handler = new LoginCommandHandler(
            _userRepository,
            _passwordHasher,
            _tokenService,
            _unitOfWork,
            _timeProvider);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new LoginCommand("alice@example.com", "WrongPassword!"), CancellationToken.None));

        Assert.Equal("Invalid email or password.", ex.Message);
        Assert.Equal(1, user.FailedLoginCount);

        // Save before throw: UnitOfWork must have been called to persist FailedLoginCount!
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_WithUnknownEmail_VerifiesDummyHashAndThrowsSameGenericMessage()
    {
        _userRepository.GetByEmailAsync(Email.Create("unknown@example.com"), Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var handler = new LoginCommandHandler(
            _userRepository,
            _passwordHasher,
            _tokenService,
            _unitOfWork,
            _timeProvider);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new LoginCommand("unknown@example.com", "SomePassword!"), CancellationToken.None));

        Assert.Equal("Invalid email or password.", ex.Message);
        _passwordHasher.Received(1).Verify("SomePassword!", Arg.Any<PasswordHash>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAccessToken_WhenValidToken_RotatesAndReturnsNewTokens()
    {
        var user = User.Register(
            Email.Create("alice@example.com"),
            "Alice",
            PasswordHash.Create(ValidHashedPassword),
            Role.Customer,
            FixedNow.AddHours(-2));
        user.AddRefreshToken("old-hash", FixedNow.AddDays(7), FixedNow.AddHours(-1));

        _tokenService.HashRefreshToken("raw-old-token").Returns("old-hash");
        _userRepository.GetByRefreshTokenHashAsync("old-hash", Arg.Any<CancellationToken>())
            .Returns(user);
        _tokenService.CreateRefreshToken(FixedNow)
            .Returns(new RefreshTokenPair("raw-new-token", "new-hash", FixedNow.AddDays(7)));
        _tokenService.CreateAccessToken(user, FixedNow)
            .Returns(new AccessTokenResult("new-jwt-token", FixedNow.AddMinutes(15), 900));

        var handler = new RefreshAccessTokenCommandHandler(
            _userRepository,
            _tokenService,
            _unitOfWork,
            _timeProvider);

        var response = await handler.Handle(
            new RefreshAccessTokenCommand("raw-old-token"),
            CancellationToken.None);

        Assert.Equal("new-jwt-token", response.AccessToken);
        Assert.Equal("raw-new-token", response.RefreshToken);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefreshAccessToken_WhenRotatedTokenIsReused_SavesRevocationOfAllTokensBeforeThrowingUnauthorizedException()
    {
        var user = User.Register(
            Email.Create("alice@example.com"),
            "Alice",
            PasswordHash.Create(ValidHashedPassword),
            Role.Customer,
            FixedNow.AddHours(-2));

        // Add token 1 and rotate it to token 2
        user.AddRefreshToken("hash-1", FixedNow.AddDays(7), FixedNow.AddHours(-2));
        user.RotateRefreshToken("hash-1", "hash-2", FixedNow.AddDays(7), FixedNow.AddHours(-1));

        _tokenService.HashRefreshToken("replayed-raw-token-1").Returns("hash-1");
        _userRepository.GetByRefreshTokenHashAsync("hash-1", Arg.Any<CancellationToken>())
            .Returns(user);
        _tokenService.CreateRefreshToken(FixedNow)
            .Returns(new RefreshTokenPair("raw-token-3", "hash-3", FixedNow.AddDays(7)));

        var handler = new RefreshAccessTokenCommandHandler(
            _userRepository,
            _tokenService,
            _unitOfWork,
            _timeProvider);

        await Assert.ThrowsAsync<UnauthorizedException>(() =>
            handler.Handle(new RefreshAccessTokenCommand("replayed-raw-token-1"), CancellationToken.None));

        // All tokens on the user (including hash-2) must be revoked AND saved before throwing 401!
        Assert.All(user.RefreshTokens, t => Assert.True(t.IsRevoked));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeRefreshToken_WhenTokenIsUnknown_IsIdempotentAndDoesNotThrow()
    {
        _tokenService.HashRefreshToken("unknown-raw").Returns("unknown-hash");
        _userRepository.GetByRefreshTokenHashAsync("unknown-hash", Arg.Any<CancellationToken>())
            .Returns((User?)null);

        var handler = new RevokeRefreshTokenCommandHandler(
            _userRepository,
            _tokenService,
            _unitOfWork,
            _timeProvider);

        await handler.Handle(new RevokeRefreshTokenCommand("unknown-raw"), CancellationToken.None);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangePassword_WithCorrectCurrentPassword_UpdatesHashAndRevokesTokens()
    {
        var user = User.Register(
            Email.Create("alice@example.com"),
            "Alice",
            PasswordHash.Create(ValidHashedPassword),
            Role.Customer,
            FixedNow.AddHours(-1));
        user.AddRefreshToken("active-token-hash", FixedNow.AddDays(7), FixedNow.AddMinutes(-30));

        const string newHashed = "AQAAAAIAAYagAAAAENewHashedPasswordValue9876543210==";
        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        _passwordHasher.Verify("OldPass@123456!", user.PasswordHash).Returns(true);
        _passwordHasher.Hash("NewPass@123456!").Returns(newHashed);

        var handler = new ChangePasswordCommandHandler(
            _userRepository,
            _passwordHasher,
            _unitOfWork,
            _timeProvider);

        await handler.Handle(
            new ChangePasswordCommand(user.Id, "OldPass@123456!", "NewPass@123456!"),
            CancellationToken.None);

        Assert.Equal(newHashed, user.PasswordHash.Value);
        Assert.All(user.RefreshTokens, t => Assert.True(t.IsRevoked));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAndActivateUser_UpdatesUserStateAndSaves()
    {
        var user = User.Register(
            Email.Create("alice@example.com"),
            "Alice",
            PasswordHash.Create(ValidHashedPassword),
            Role.Customer,
            FixedNow.AddHours(-1));

        _userRepository.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);

        var deactivateHandler = new DeactivateUserCommandHandler(_userRepository, _unitOfWork, _timeProvider);
        await deactivateHandler.Handle(new DeactivateUserCommand(user.Id), CancellationToken.None);
        Assert.False(user.IsActive);

        var activateHandler = new ActivateUserCommandHandler(_userRepository, _unitOfWork, _timeProvider);
        await activateHandler.Handle(new ActivateUserCommand(user.Id), CancellationToken.None);
        Assert.True(user.IsActive);

        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
