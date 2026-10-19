using OrderService.Domain.Abstractions;

namespace OrderService.Domain.Orders;

public sealed record Quantity
{
    public const int MinValue = 1;
    public const int MaxValue = 100;

    public int Value { get; }

    private Quantity(int value)
    {
        Value = value;
    }

    public static Quantity Create(int value)
    {
        if (value < MinValue || value > MaxValue)
        {
            throw new DomainException(
                $"Quantity must be between {MinValue} and {MaxValue}.",
                "order.invalid_quantity");
        }

        return new Quantity(value);
    }
}
