using UserService.Application.Abstractions;
using UserService.Application.Exceptions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.Application.Users.Commands.Login;

public sealed class LoginCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<LoginCommand, AuthTokensResponse>
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";
    private static readonly PasswordHash DummyPasswordHash =
        PasswordHash.Create("AQAAAAIAAYagAAAAEDummyPasswordHashForTimingProtection1234567890==");

    public async Task<AuthTokensResponse> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();

        Email email;
        try
        {
            email = Email.Create(request.Email);
        }
        catch (DomainException)
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        var user = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            // Verify against a dummy hash so unknown-email requests pay a similar CPU cost.
            _ = passwordHasher.Verify(request.Password, DummyPasswordHash);
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        if (!user.CanLogin(utcNow))
        {
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        if (!passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            // Save before throw: persist the incremented FailedLoginCount (and any lockout) before returning 401.
            user.RecordFailedLogin(utcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedException(InvalidCredentialsMessage);
        }

        user.RecordSuccessfulLogin(utcNow);

        var refreshPair = tokenService.CreateRefreshToken(utcNow);
        user.AddRefreshToken(refreshPair.TokenHash, refreshPair.ExpiresAtUtc, utcNow);

        var accessResult = tokenService.CreateAccessToken(user, utcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthTokensResponse(
            accessResult.AccessToken,
            "Bearer",
            accessResult.ExpiresInSeconds,
            refreshPair.RawToken,
            refreshPair.ExpiresAtUtc,
            UserResponse.FromDomain(user));
    }
}
