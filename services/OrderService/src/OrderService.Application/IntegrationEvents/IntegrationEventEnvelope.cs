namespace OrderService.Application.IntegrationEvents;

public sealed record IntegrationEventEnvelope<T>(
    Guid MessageId,
    string EventType,
    int SchemaVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    Guid? CausationId,
    string Producer,
    T Data);
