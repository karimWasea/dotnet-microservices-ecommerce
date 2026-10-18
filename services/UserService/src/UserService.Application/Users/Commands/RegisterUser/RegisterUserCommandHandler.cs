using UserService.Application.Abstractions;
using UserService.Application.Exceptions;
using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.Application.Users.Commands.RegisterUser;

public sealed class RegisterUserCommandHandler(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<RegisterUserCommand, UserResponse>
{
    public async Task<UserResponse> Handle(
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow();
        var email = Email.Create(request.Email);

        if (await userRepository.ExistsByEmailAsync(email, cancellationToken))
        {
            throw new ConflictException($"A user with email '{email.Value}' already exists.");
        }

        var passwordHash = PasswordHash.Create(passwordHasher.Hash(request.Password));
        var user = User.Register(email, request.DisplayName, passwordHash, Role.Customer, utcNow);

        userRepository.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return UserResponse.FromDomain(user);
    }
}
