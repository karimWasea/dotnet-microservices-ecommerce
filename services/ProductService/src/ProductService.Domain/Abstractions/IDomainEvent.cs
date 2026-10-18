using MediatR;

namespace ProductService.Domain.Abstractions;

public interface IDomainEvent : INotification
{
    DateTimeOffset OccurredOnUtc { get; }
}
