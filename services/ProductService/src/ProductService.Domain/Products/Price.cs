using ProductService.Domain.Abstractions;

namespace ProductService.Domain.Products;

public sealed record Price
{
    public decimal Amount { get; }

    private Price(decimal amount)
    {
        Amount = amount;
    }

    public static Price Create(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainException("Product price must be greater than zero.", "price.not_positive");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Product price cannot have more than 2 decimal places.", "price.too_many_decimals");
        }

        return new Price(amount);
    }
}
