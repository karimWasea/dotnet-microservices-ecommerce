using ProductService.Domain.Abstractions;

namespace ProductService.Domain.Products;

public sealed record Sku
{
    public const int MaxLength = 50;

    public string Value { get; }

    private Sku(string value)
    {
        Value = value;
    }

    public static Sku Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Product SKU is required.", "sku.empty");
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length > MaxLength)
        {
            throw new DomainException($"Product SKU cannot exceed {MaxLength} characters.", "sku.too_long");
        }

        return new Sku(normalized);
    }

    public override string ToString() => Value;
}
