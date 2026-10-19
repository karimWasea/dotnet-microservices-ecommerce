namespace OrderService.Application.IntegrationEvents.Incoming;

public sealed record StockReservedIntegrationEvent(
    Guid OrderId,
    DateTimeOffset OccurredAtUtc)
{
    public const string RoutingKey = "inventory.reserved";
}

public sealed record StockReservationFailedIntegrationEvent(
    Guid OrderId,
    string Reason,
    DateTimeOffset OccurredAtUtc)
{
    public const string RoutingKey = "inventory.reservation-failed";
}

public sealed record PaymentSucceededIntegrationEvent(
    Guid OrderId,
    string PaymentId,
    decimal Amount,
    string Currency,
    DateTimeOffset OccurredAtUtc)
{
    public const string RoutingKey = "payment.succeeded";
}

public sealed record PaymentFailedIntegrationEvent(
    Guid OrderId,
    string Reason,
    DateTimeOffset OccurredAtUtc)
{
    public const string RoutingKey = "payment.failed";
}
