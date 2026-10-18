using UserService.Application.Abstractions;
using UserService.Application.Exceptions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.Application.Users.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException($"User with id '{request.UserId}' was not found.");

        if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new UnauthorizedException("Current password is incorrect.");
        }

        var newPasswordHash = PasswordHash.Create(passwordHasher.Hash(request.NewPassword));
        user.ChangePassword(newPasswordHash, timeProvider.GetUtcNow());

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
