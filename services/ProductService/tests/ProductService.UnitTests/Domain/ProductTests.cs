using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;
using ProductService.Domain.Products.Events;

namespace ProductService.UnitTests.Domain;

public sealed class ProductTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(10);
    private static readonly DateTimeOffset T2 = T0.AddMinutes(20);

    private static Product CreateSampleProduct() =>
        Product.Create(
            name: "  Mechanical Keyboard  ",
            description: "  Wireless RGB keyboard  ",
            sku: Sku.Create("kb-001"),
            price: Price.Create(129.99m),
            category: "  Peripherals  ",
            utcNow: T0);

    [Fact]
    public void Create_WithValidArguments_SetsPropertiesAndRaisesProductCreatedEvent()
    {
        var product = CreateSampleProduct();

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("Mechanical Keyboard", product.Name);
        Assert.Equal("Wireless RGB keyboard", product.Description);
        Assert.Equal("KB-001", product.Sku.Value);
        Assert.Equal(129.99m, product.Price.Amount);
        Assert.Equal("Peripherals", product.Category);
        Assert.True(product.IsActive);
        Assert.Equal(T0, product.CreatedAtUtc);
        Assert.Equal(T0, product.UpdatedAtUtc);

        var evt = Assert.Single(product.DomainEvents);
        var created = Assert.IsType<ProductCreated>(evt);
        Assert.Equal(product.Id, created.ProductId);
        Assert.Equal("KB-001", created.Sku);
        Assert.Equal(129.99m, created.Price);
        Assert.Equal(T0, created.OccurredOnUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenNameIsBlank_ThrowsDomainException(string invalidName)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create(invalidName, "Desc", Sku.Create("KB-1"), Price.Create(10m), "Cat", T0));
        Assert.Equal("product.name_required", ex.Code);
    }

    [Fact]
    public void Create_WhenNameTooLong_ThrowsDomainException()
    {
        var longName = new string('X', Product.MaxNameLength + 1);
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create(longName, "Desc", Sku.Create("KB-1"), Price.Create(10m), "Cat", T0));
        Assert.Equal("product.name_too_long", ex.Code);
    }

    [Fact]
    public void Create_WhenDescriptionTooLong_ThrowsDomainException()
    {
        var longDesc = new string('D', Product.MaxDescriptionLength + 1);
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create("Name", longDesc, Sku.Create("KB-1"), Price.Create(10m), "Cat", T0));
        Assert.Equal("product.description_too_long", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenCategoryIsBlank_ThrowsDomainException(string invalidCategory)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create("Name", "Desc", Sku.Create("KB-1"), Price.Create(10m), invalidCategory, T0));
        Assert.Equal("product.category_required", ex.Code);
    }

    [Fact]
    public void Create_WhenCategoryTooLong_ThrowsDomainException()
    {
        var longCat = new string('C', Product.MaxCategoryLength + 1);
        var ex = Assert.Throws<DomainException>(() =>
            Product.Create("Name", "Desc", Sku.Create("KB-1"), Price.Create(10m), longCat, T0));
        Assert.Equal("product.category_too_long", ex.Code);
    }

    [Fact]
    public void ChangePrice_WhenPriceIsDifferent_UpdatesPriceAndRaisesProductPriceChangedEvent()
    {
        var product = CreateSampleProduct();
        product.ClearDomainEvents();

        product.ChangePrice(Price.Create(149.99m), T1);

        Assert.Equal(149.99m, product.Price.Amount);
        Assert.Equal(T1, product.UpdatedAtUtc);

        var evt = Assert.Single(product.DomainEvents);
        var changed = Assert.IsType<ProductPriceChanged>(evt);
        Assert.Equal(product.Id, changed.ProductId);
        Assert.Equal(129.99m, changed.OldPrice);
        Assert.Equal(149.99m, changed.NewPrice);
        Assert.Equal(T1, changed.OccurredOnUtc);
    }

    [Fact]
    public void ChangePrice_WhenPriceIsSame_DoesNothingAndRaisesNoEvent()
    {
        var product = CreateSampleProduct();
        product.ClearDomainEvents();

        product.ChangePrice(Price.Create(129.99m), T1);

        Assert.Equal(129.99m, product.Price.Amount);
        Assert.Equal(T0, product.UpdatedAtUtc);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public void UpdateDetails_And_ChangePrice_WhenProductIsInactive_ThrowDomainException()
    {
        var product = CreateSampleProduct();
        product.Deactivate(T1);

        var ex1 = Assert.Throws<DomainException>(() =>
            product.UpdateDetails("New Name", "New Desc", "New Cat", T2));
        Assert.Equal("product.inactive", ex1.Code);

        var ex2 = Assert.Throws<DomainException>(() =>
            product.ChangePrice(Price.Create(99.99m), T2));
        Assert.Equal("product.inactive", ex2.Code);
    }

    [Fact]
    public void Deactivate_And_Activate_AreIdempotentAndRaiseEventsOnlyOnStateTransition()
    {
        var product = CreateSampleProduct();
        product.ClearDomainEvents();

        // First Deactivate -> transitions to inactive and raises ProductDeactivated
        product.Deactivate(T1);
        Assert.False(product.IsActive);
        Assert.Equal(T1, product.UpdatedAtUtc);
        var deactivatedEvt = Assert.Single(product.DomainEvents);
        Assert.IsType<ProductDeactivated>(deactivatedEvt);

        product.ClearDomainEvents();

        // Second Deactivate -> already inactive, does nothing, raises no event
        product.Deactivate(T2);
        Assert.False(product.IsActive);
        Assert.Equal(T1, product.UpdatedAtUtc);
        Assert.Empty(product.DomainEvents);

        // First Activate -> transitions to active and raises ProductActivated
        product.Activate(T2);
        Assert.True(product.IsActive);
        Assert.Equal(T2, product.UpdatedAtUtc);
        var activatedEvt = Assert.Single(product.DomainEvents);
        Assert.IsType<ProductActivated>(activatedEvt);

        product.ClearDomainEvents();

        // Second Activate -> already active, does nothing, raises no event
        product.Activate(T2.AddMinutes(5));
        Assert.True(product.IsActive);
        Assert.Equal(T2, product.UpdatedAtUtc);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public void UpdateDetails_WhenNothingChanged_DoesNotBumpUpdatedAtUtc()
    {
        var product = CreateSampleProduct();

        product.UpdateDetails("  Mechanical Keyboard  ", "  Wireless RGB keyboard  ", "  Peripherals  ", T1);

        Assert.Equal(T0, product.UpdatedAtUtc);
    }

    [Fact]
    public void Update_UpdatesDetailsAndPriceInSingleCall()
    {
        var product = CreateSampleProduct();
        product.ClearDomainEvents();

        product.Update("Mechanical Keyboard Pro", "Updated description", "Peripherals", Price.Create(159.99m), T1);

        Assert.Equal("Mechanical Keyboard Pro", product.Name);
        Assert.Equal("Updated description", product.Description);
        Assert.Equal(159.99m, product.Price.Amount);
        Assert.Equal(T1, product.UpdatedAtUtc);
        var evt = Assert.Single(product.DomainEvents);
        Assert.IsType<ProductPriceChanged>(evt);
    }
}

