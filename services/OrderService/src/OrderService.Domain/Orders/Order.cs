using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders.Events;

namespace OrderService.Domain.Orders;

public sealed class Order : AggregateRoot
{
    public const int MinLines = 1;
    public const int MaxLines = 20;
    public const int MaxReasonLength = 200;

    private readonly List<OrderLine> _lines = [];

    public Guid UserId { get; private set; }
    public OrderStatus Status { get; private set; }
    public Address ShippingAddress { get; private set; } = null!;
    public Money Total { get; private set; } = null!;
    public string? StatusReason { get; private set; }
    public CancelledBy? CancelledBy { get; private set; }
    public string? PaymentId { get; private set; }
    public DateTimeOffset PlacedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyCollection<OrderLine> Lines => _lines.AsReadOnly();

    // Required by EF Core.
    private Order()
    {
    }

    private Order(
        Guid id,
        Guid userId,
        Address shippingAddress,
        Money total,
        List<OrderLine> lines,
        DateTimeOffset utcNow)
        : base(id)
    {
        UserId = userId;
        Status = OrderStatus.Pending;
        ShippingAddress = shippingAddress;
        Total = total;
        PlacedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
        Version = 1;
        _lines = lines;
    }

    public static Order Place(
        Guid userId,
        Address shippingAddress,
        IReadOnlyList<OrderLineDraft> lines,
        string currency,
        DateTimeOffset utcNow)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("UserId is required.", "order.invalid_user_id");
        }

        if (shippingAddress is null)
        {
            throw new DomainException("ShippingAddress is required.", "order.invalid_address");
        }

        if (lines is null || lines.Count < MinLines || lines.Count > MaxLines)
        {
            throw new DomainException(
                $"An order must contain between {MinLines} and {MaxLines} line items.",
                "order.invalid_line_count");
        }

        var normalizedCurrency = Money.ValidateCurrency(currency);
        var orderId = Guid.CreateVersion7(utcNow);

        var seenProducts = new HashSet<Guid>();
        var orderLines = new List<OrderLine>(lines.Count);
        var runningTotal = Money.Zero(normalizedCurrency);

        foreach (var draft in lines)
        {
            if (draft is null)
            {
                throw new DomainException("Order line item cannot be null.", "order.invalid_line_count");
            }

            if (!seenProducts.Add(draft.ProductId))
            {
                throw new DomainException(
                    $"Duplicate ProductId '{draft.ProductId}' is not allowed in the same order.",
                    "order.duplicate_product");
            }

            var line = OrderLine.Create(
                orderId,
                draft.ProductId,
                draft.ProductName,
                draft.UnitPrice,
                draft.Quantity,
                utcNow);

            runningTotal = runningTotal.Add(line.LineTotal);
            orderLines.Add(line);
        }

        if (runningTotal.Amount <= 0)
        {
            throw new DomainException("Order total must be greater than zero.", "order.invalid_total");
        }

        var order = new Order(
            orderId,
            userId,
            shippingAddress,
            runningTotal,
            orderLines,
            utcNow);

        var lineSnapshots = orderLines
            .Select(l => new OrderPlacedLineSnapshot(
                l.ProductId,
                l.ProductName,
                l.UnitPrice.Amount,
                l.Quantity.Value,
                l.LineTotal.Amount))
            .ToList();

        var addressSnapshot = new OrderPlacedAddressSnapshot(
            shippingAddress.RecipientName,
            shippingAddress.Street,
            shippingAddress.City,
            shippingAddress.Country);

        order.RaiseDomainEvent(new OrderPlacedDomainEvent(
            order.Id,
            order.UserId,
            order.Version,
            order.Status.ToString(),
            lineSnapshots,
            addressSnapshot,
            order.Total.Amount,
            order.Total.Currency,
            utcNow));

        return order;
    }

    public TransitionResult MarkStockReserved(DateTimeOffset utcNow)
    {
        switch (Status)
        {
            case OrderStatus.Pending:
                Status = OrderStatus.StockReserved;
                Version++;
                UpdatedAtUtc = utcNow;
                RaiseDomainEvent(new OrderStockReservedDomainEvent(Id, UserId, Version, utcNow));
                return TransitionResult.Applied;

            case OrderStatus.StockReserved:
            case OrderStatus.Confirmed:
                return TransitionResult.NoChange;

            case OrderStatus.Rejected:
            case OrderStatus.Cancelled:
            default:
                return TransitionResult.NotAllowed;
        }
    }

    public TransitionResult Confirm(string? paymentId, DateTimeOffset utcNow)
    {
        switch (Status)
        {
            case OrderStatus.Pending:
            case OrderStatus.StockReserved:
                Status = OrderStatus.Confirmed;
                PaymentId = string.IsNullOrWhiteSpace(paymentId) ? "unknown" : paymentId.Trim();
                Version++;
                UpdatedAtUtc = utcNow;
                RaiseDomainEvent(new OrderConfirmedDomainEvent(
                    Id,
                    UserId,
                    Version,
                    Total.Amount,
                    Total.Currency,
                    PaymentId,
                    utcNow));
                return TransitionResult.Applied;

            case OrderStatus.Confirmed:
                return TransitionResult.NoChange;

            case OrderStatus.Rejected:
            case OrderStatus.Cancelled:
            default:
                return TransitionResult.NotAllowed;
        }
    }

    public TransitionResult Reject(string? reason, DateTimeOffset utcNow)
    {
        switch (Status)
        {
            case OrderStatus.Pending:
                Status = OrderStatus.Rejected;
                StatusReason = NormalizeReason(reason, "StockReservationFailed");
                Version++;
                UpdatedAtUtc = utcNow;
                RaiseDomainEvent(new OrderRejectedDomainEvent(
                    Id,
                    UserId,
                    Version,
                    StatusReason,
                    utcNow));
                return TransitionResult.Applied;

            case OrderStatus.Rejected:
                return TransitionResult.NoChange;

            case OrderStatus.StockReserved:
            case OrderStatus.Confirmed:
            case OrderStatus.Cancelled:
            default:
                return TransitionResult.NotAllowed;
        }
    }

    public TransitionResult Cancel(string? reason, CancelledBy cancelledBy, DateTimeOffset utcNow)
    {
        switch (Status)
        {
            case OrderStatus.Pending:
            case OrderStatus.StockReserved:
                Status = OrderStatus.Cancelled;
                StatusReason = NormalizeReason(reason, "Cancelled");
                CancelledBy = cancelledBy;
                Version++;
                UpdatedAtUtc = utcNow;
                RaiseDomainEvent(new OrderCancelledDomainEvent(
                    Id,
                    UserId,
                    Version,
                    StatusReason,
                    cancelledBy.ToString(),
                    utcNow));
                return TransitionResult.Applied;

            case OrderStatus.Cancelled:
                return TransitionResult.NoChange;

            case OrderStatus.Confirmed:
            case OrderStatus.Rejected:
            default:
                return TransitionResult.NotAllowed;
        }
    }

    private static string NormalizeReason(string? reason, string fallback)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return fallback;
        }

        var trimmed = reason.Trim();
        return trimmed.Length <= MaxReasonLength
            ? trimmed
            : trimmed[..MaxReasonLength];
    }
}
