using UserService.Application.Abstractions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.Application.Users.Commands.RevokeRefreshToken;

public sealed class RevokeRefreshTokenCommandHandler(
    IUserRepository userRepository,
    ITokenService tokenService,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<RevokeRefreshTokenCommand>
{
    public async Task Handle(
        RevokeRefreshTokenCommand request,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
        var user = await userRepository.GetByRefreshTokenHashAsync(tokenHash, cancellationToken);

        // Idempotent: if the token is unknown, succeed without error.
        if (user is null)
        {
            return;
        }

        user.RevokeRefreshToken(tokenHash, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
