namespace OrderService.Application.Abstractions;

public interface IInboxMessage
{
    Guid MessageId { get; }
    string ConsumerName { get; }
}

public interface IInbox
{
    Task<bool> HasProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
    Task MarkProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default);
}
