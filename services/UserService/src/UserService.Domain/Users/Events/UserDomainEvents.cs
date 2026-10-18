using UserService.Domain.Abstractions;

namespace UserService.Domain.Users.Events;

public sealed record UserRegisteredDomainEvent(
    Guid UserId,
    string Email,
    string Role,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record UserLockedOutDomainEvent(
    Guid UserId,
    DateTimeOffset LockedOutUntilUtc,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record RefreshTokenReuseDetectedDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record PasswordChangedDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record UserDeactivatedDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record UserActivatedDomainEvent(
    Guid UserId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
