using MediatR;
using Microsoft.Extensions.Logging;
using UserService.Domain.Users.Events;

namespace UserService.Application.Users.EventHandlers;

public sealed class UserRegisteredDomainEventHandler(
    ILogger<UserRegisteredDomainEventHandler> logger)
    : INotificationHandler<UserRegisteredDomainEvent>
{
    public Task Handle(UserRegisteredDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Domain event handled: UserRegistered for UserId={UserId}, Email={Email}, Role={Role}",
            notification.UserId,
            notification.Email,
            notification.Role);
        return Task.CompletedTask;
    }
}

public sealed class UserLockedOutDomainEventHandler(
    ILogger<UserLockedOutDomainEventHandler> logger)
    : INotificationHandler<UserLockedOutDomainEvent>
{
    public Task Handle(UserLockedOutDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Domain event handled: UserLockedOut for UserId={UserId} until {LockedOutUntilUtc}",
            notification.UserId,
            notification.LockedOutUntilUtc);
        return Task.CompletedTask;
    }
}

public sealed class RefreshTokenReuseDetectedDomainEventHandler(
    ILogger<RefreshTokenReuseDetectedDomainEventHandler> logger)
    : INotificationHandler<RefreshTokenReuseDetectedDomainEvent>
{
    public Task Handle(RefreshTokenReuseDetectedDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogWarning(
            "Domain event handled: RefreshTokenReuseDetected for UserId={UserId}. All active refresh tokens were revoked.",
            notification.UserId);
        return Task.CompletedTask;
    }
}

public sealed class PasswordChangedDomainEventHandler(
    ILogger<PasswordChangedDomainEventHandler> logger)
    : INotificationHandler<PasswordChangedDomainEvent>
{
    public Task Handle(PasswordChangedDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Domain event handled: PasswordChanged for UserId={UserId}",
            notification.UserId);
        return Task.CompletedTask;
    }
}

public sealed class UserDeactivatedDomainEventHandler(
    ILogger<UserDeactivatedDomainEventHandler> logger)
    : INotificationHandler<UserDeactivatedDomainEvent>
{
    public Task Handle(UserDeactivatedDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Domain event handled: UserDeactivated for UserId={UserId}",
            notification.UserId);
        return Task.CompletedTask;
    }
}

public sealed class UserActivatedDomainEventHandler(
    ILogger<UserActivatedDomainEventHandler> logger)
    : INotificationHandler<UserActivatedDomainEvent>
{
    public Task Handle(UserActivatedDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Domain event handled: UserActivated for UserId={UserId}",
            notification.UserId);
        return Task.CompletedTask;
    }
}
