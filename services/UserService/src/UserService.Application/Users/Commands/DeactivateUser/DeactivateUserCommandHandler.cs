using UserService.Application.Abstractions;
using UserService.Application.Exceptions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.Application.Users.Commands.DeactivateUser;

public sealed class DeactivateUserCommandHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<DeactivateUserCommand>
{
    public async Task Handle(
        DeactivateUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException($"User with id '{request.UserId}' was not found.");

        user.Deactivate(timeProvider.GetUtcNow());

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
