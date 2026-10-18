using UserService.Application.Abstractions;
using UserService.Application.Exceptions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.Application.Users.Commands.RefreshAccessToken;

public sealed class RefreshAccessTokenCommandHandler(
    IUserRepository userRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<RefreshAccessTokenCommand, AuthTokensResponse>
{
    private const string InvalidRefreshTokenMessage = "Invalid or expired refresh token.";

    public async Task<AuthTokensResponse> Handle(
        RefreshAccessTokenCommand request,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();
        var presentedHash = tokenService.HashRefreshToken(request.RefreshToken);

        var user = await userRepository.GetByRefreshTokenHashAsync(presentedHash, cancellationToken);
        if (user is null)
        {
            throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }

        var newRefreshPair = tokenService.CreateRefreshToken(utcNow);

        var outcome = user.RotateRefreshToken(
            presentedHash,
            newRefreshPair.TokenHash,
            newRefreshPair.ExpiresAtUtc,
            utcNow);

        switch (outcome)
        {
            case RefreshOutcome.ReuseDetected:
                // Save before throw: commit the revocation of ALL refresh tokens before returning 401.
                await unitOfWork.SaveChangesAsync(cancellationToken);
                throw new UnauthorizedException(InvalidRefreshTokenMessage);

            case RefreshOutcome.Invalid:
            case RefreshOutcome.Expired:
                throw new UnauthorizedException(InvalidRefreshTokenMessage);

            case RefreshOutcome.Rotated:
                var accessResult = tokenService.CreateAccessToken(user, utcNow);
                await unitOfWork.SaveChangesAsync(cancellationToken);

                return new AuthTokensResponse(
                    accessResult.AccessToken,
                    "Bearer",
                    accessResult.ExpiresInSeconds,
                    newRefreshPair.RawToken,
                    newRefreshPair.ExpiresAtUtc,
                    UserResponse.FromDomain(user));

            default:
                throw new UnauthorizedException(InvalidRefreshTokenMessage);
        }
    }
}
