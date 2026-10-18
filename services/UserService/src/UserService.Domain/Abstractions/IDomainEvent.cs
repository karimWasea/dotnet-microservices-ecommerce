using MediatR;

namespace UserService.Domain.Abstractions;

public interface IDomainEvent : INotification
{
    DateTimeOffset OccurredOnUtc { get; }
}
