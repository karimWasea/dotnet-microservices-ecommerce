using OrderService.Domain.Abstractions;

namespace OrderService.Domain.Orders.Events;

public sealed record OrderPlacedLineSnapshot(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record OrderPlacedAddressSnapshot(
    string RecipientName,
    string Street,
    string City,
    string Country);

public sealed record OrderPlacedDomainEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    string Status,
    IReadOnlyList<OrderPlacedLineSnapshot> Items,
    OrderPlacedAddressSnapshot ShippingAddress,
    decimal Total,
    string Currency,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record OrderStockReservedDomainEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record OrderConfirmedDomainEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    decimal Total,
    string Currency,
    string PaymentId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record OrderRejectedDomainEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    string Reason,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;

public sealed record OrderCancelledDomainEvent(
    Guid OrderId,
    Guid UserId,
    int Version,
    string Reason,
    string CancelledBy,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
