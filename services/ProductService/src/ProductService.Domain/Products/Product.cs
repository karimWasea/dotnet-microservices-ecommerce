using ProductService.Domain.Abstractions;
using ProductService.Domain.Products.Events;

namespace ProductService.Domain.Products;

public sealed class Product : AggregateRoot
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxCategoryLength = 100;

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Sku Sku { get; private set; } = null!;
    public Price Price { get; private set; } = null!;
    public string Category { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public uint Version { get; private set; }

    // Required by EF Core.
    private Product()
    {
    }

    private Product(
        Guid id,
        string name,
        string? description,
        Sku sku,
        Price price,
        string category,
        DateTimeOffset utcNow) : base(id)
    {
        Name = name;
        Description = description;
        Sku = sku;
        Price = price;
        Category = category;
        IsActive = true;
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public static Product Create(
        string name,
        string? description,
        Sku sku,
        Price price,
        string category,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(sku);
        ArgumentNullException.ThrowIfNull(price);

        var validName = ValidateName(name);
        var validDescription = ValidateDescription(description);
        var validCategory = ValidateCategory(category);

        var product = new Product(
            Guid.CreateVersion7(utcNow),
            validName,
            validDescription,
            sku,
            price,
            validCategory,
            utcNow);

        product.RaiseDomainEvent(new ProductCreated(
            product.Id,
            product.Sku.Value,
            product.Price.Amount,
            utcNow));

        return product;
    }

    public void Update(
        string name,
        string? description,
        string category,
        Price newPrice,
        DateTimeOffset utcNow)
    {
        UpdateDetails(name, description, category, utcNow);
        ChangePrice(newPrice, utcNow);
    }

    public void UpdateDetails(
        string name,
        string? description,
        string category,
        DateTimeOffset utcNow)
    {
        EnsureActive();

        var validName = ValidateName(name);
        var validDescription = ValidateDescription(description);
        var validCategory = ValidateCategory(category);

        if (Name == validName && Description == validDescription && Category == validCategory)
        {
            return;
        }

        Name = validName;
        Description = validDescription;
        Category = validCategory;
        UpdatedAtUtc = utcNow;
    }

    public void ChangePrice(Price newPrice, DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(newPrice);
        EnsureActive();

        if (Price == newPrice)
        {
            return;
        }

        var oldAmount = Price.Amount;
        Price = newPrice;
        UpdatedAtUtc = utcNow;

        RaiseDomainEvent(new ProductPriceChanged(Id, oldAmount, newPrice.Amount, utcNow));
    }

    public void Deactivate(DateTimeOffset utcNow)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAtUtc = utcNow;
        RaiseDomainEvent(new ProductDeactivated(Id, utcNow));
    }

    public void Activate(DateTimeOffset utcNow)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAtUtc = utcNow;
        RaiseDomainEvent(new ProductActivated(Id, utcNow));
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new DomainException(
                "Cannot modify an inactive product. Activate it first.",
                "product.inactive");
        }
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Product name is required.", "product.name_required");
        }

        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new DomainException(
                $"Product name cannot exceed {MaxNameLength} characters.",
                "product.name_too_long");
        }

        return trimmed;
    }

    private static string? ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        if (trimmed.Length > MaxDescriptionLength)
        {
            throw new DomainException(
                $"Product description cannot exceed {MaxDescriptionLength} characters.",
                "product.description_too_long");
        }

        return trimmed;
    }

    private static string ValidateCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new DomainException("Product category is required.", "product.category_required");
        }

        var trimmed = category.Trim();
        if (trimmed.Length > MaxCategoryLength)
        {
            throw new DomainException(
                $"Product category cannot exceed {MaxCategoryLength} characters.",
                "product.category_too_long");
        }

        return trimmed;
    }
}
