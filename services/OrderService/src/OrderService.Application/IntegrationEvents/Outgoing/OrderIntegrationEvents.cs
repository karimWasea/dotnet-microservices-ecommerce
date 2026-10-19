namespace OrderService.Application.IntegrationEvents.Outgoing;

public sealed record OrderLineIntegrationDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record AddressIntegrationDto(
    string RecipientName,
    string Street,
    string City,
    string Country);

public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    string Status,
    IReadOnlyList<OrderLineIntegrationDto> Items,
    AddressIntegrationDto ShippingAddress,
    decimal Total,
    string Currency,
    DateTimeOffset PlacedAtUtc)
{
    public const string RoutingKey = "order.placed";
}

public sealed record OrderStockReservedIntegrationEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    DateTimeOffset OccurredAtUtc)
{
    public const string RoutingKey = "order.stock-reserved";
}

public sealed record OrderConfirmedIntegrationEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    decimal Total,
    string Currency,
    string PaymentId,
    DateTimeOffset OccurredAtUtc)
{
    public const string RoutingKey = "order.confirmed";
}

public sealed record OrderRejectedIntegrationEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    string Reason,
    DateTimeOffset OccurredAtUtc)
{
    public const string RoutingKey = "order.rejected";
}

public sealed record OrderCancelledIntegrationEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    string Reason,
    string CancelledBy,
    DateTimeOffset OccurredAtUtc)
{
    public const string RoutingKey = "order.cancelled";
}
