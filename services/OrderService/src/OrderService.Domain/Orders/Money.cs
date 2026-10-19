using OrderService.Domain.Abstractions;

namespace OrderService.Domain.Orders;

public sealed record Money
{
    public const int CurrencyLength = 3;

    public decimal Amount { get; }
    public string Currency { get; }

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Create(decimal amount, string? currency)
    {
        if (amount < 0)
        {
            throw new DomainException("Money amount cannot be negative.", "order.invalid_money");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Money amount can have at most 2 decimal places.", "order.invalid_money");
        }

        var normalizedCurrency = ValidateCurrency(currency);

        return new Money(amount, normalizedCurrency);
    }

    public static Money Zero(string? currency) => Create(0m, currency);

    public Money Add(Money other)
    {
        if (other is null)
        {
            throw new DomainException("Money operand is required.", "order.invalid_money");
        }

        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new DomainException(
                $"Cannot combine money with different currencies ('{Currency}' and '{other.Currency}').",
                "order.currency_mismatch");
        }

        return new Money(Amount + other.Amount, Currency);
    }

    public Money Multiply(int multiplier)
    {
        if (multiplier < 0)
        {
            throw new DomainException("Money multiplier cannot be negative.", "order.invalid_money");
        }

        return new Money(Amount * multiplier, Currency);
    }

    public static string ValidateCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("Currency is required.", "order.invalid_money");
        }

        var trimmed = currency.Trim().ToUpperInvariant();
        if (trimmed.Length != 3 || !trimmed.All(ch => ch is >= 'A' and <= 'Z'))
        {
            throw new DomainException("Currency must be a 3-letter uppercase ISO code.", "order.invalid_money");
        }

        return trimmed;
    }
}
