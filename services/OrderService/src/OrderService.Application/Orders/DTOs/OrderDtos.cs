namespace OrderService.Application.Orders.DTOs;

public sealed record OrderLineDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    string Currency,
    int Quantity,
    decimal LineTotal);

public sealed record AddressDto(
    string RecipientName,
    string Street,
    string City,
    string Country);

public sealed record StatusHistoryEntryDto(
    string Status,
    DateTimeOffset At,
    string? Reason);

public sealed record OrderSummaryDto(
    Guid Id,
    Guid UserId,
    string Status,
    decimal Total,
    string Currency,
    int ItemCount,
    DateTimeOffset PlacedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record OrderDetailsDto(
    Guid Id,
    Guid UserId,
    string Status,
    string? StatusReason,
    string? CancelledBy,
    string? PaymentId,
    decimal Total,
    string Currency,
    int ItemCount,
    AddressDto ShippingAddress,
    IReadOnlyList<OrderLineDto> Items,
    IReadOnlyList<StatusHistoryEntryDto> History,
    DateTimeOffset PlacedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int Version);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount);

public enum PlaceOrderOutcome
{
    Created = 1,
    Replayed = 2,
    IdempotencyKeyConflict = 3,
    IdempotencyKeyInProgress = 4
}

public sealed record PlaceOrderResult(
    PlaceOrderOutcome Outcome,
    OrderDetailsDto? Order = null,
    string? ErrorMessage = null);

public enum CancelOrderOutcome
{
    Cancelled = 1,
    AlreadyCancelled = 2,
    NotFound = 3,
    Forbidden = 4,
    ConflictCannotCancel = 5
}

public sealed record CancelOrderResult(
    CancelOrderOutcome Outcome,
    string? ErrorMessage = null);
