using MediatR;

namespace OrderService.Domain.Abstractions;

public interface IDomainEvent : INotification
{
    DateTimeOffset OccurredOnUtc { get; }
}
