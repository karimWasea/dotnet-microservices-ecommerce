using OrderService.Domain.Abstractions;

namespace OrderService.Domain.Orders;

public sealed record OrderLineDraft(
    Guid ProductId,
    string ProductName,
    Money UnitPrice,
    Quantity Quantity);

public sealed class OrderLine : Entity
{
    public const int MaxProductNameLength = 200;

    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public Money UnitPrice { get; private set; } = null!;
    public Quantity Quantity { get; private set; } = null!;
    public Money LineTotal { get; private set; } = null!;

    // Required by EF Core.
    private OrderLine()
    {
    }

    internal OrderLine(
        Guid id,
        Guid orderId,
        Guid productId,
        string productName,
        Money unitPrice,
        Quantity quantity,
        Money lineTotal)
        : base(id)
    {
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        UnitPrice = unitPrice;
        Quantity = quantity;
        LineTotal = lineTotal;
    }

    internal static OrderLine Create(
        Guid orderId,
        Guid productId,
        string? productName,
        Money? unitPrice,
        Quantity? quantity,
        DateTimeOffset utcNow)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainException("ProductId is required.", "order.invalid_product_id");
        }

        if (string.IsNullOrWhiteSpace(productName))
        {
            throw new DomainException("ProductName is required.", "order.invalid_product_name");
        }

        var trimmedName = productName.Trim();
        if (trimmedName.Length > MaxProductNameLength)
        {
            throw new DomainException(
                $"ProductName must not exceed {MaxProductNameLength} characters.",
                "order.invalid_product_name");
        }

        if (unitPrice is null || unitPrice.Amount <= 0)
        {
            throw new DomainException("UnitPrice must be greater than zero.", "order.invalid_money");
        }

        if (quantity is null)
        {
            throw new DomainException("Quantity is required.", "order.invalid_quantity");
        }

        var lineTotal = unitPrice.Multiply(quantity.Value);

        return new OrderLine(
            Guid.CreateVersion7(utcNow),
            orderId,
            productId,
            trimmedName,
            unitPrice,
            quantity,
            lineTotal);
    }
}
