using OrderService.Domain.Abstractions;
using OrderService.Domain.Orders;

namespace OrderService.UnitTests.Domain;

public sealed class ValueObjectsTests
{
    [Fact]
    public void Money_Create_Add_Multiply_WorksWithValidInputs()
    {
        var price = Money.Create(250.50m, " egp ");
        var multiplied = price.Multiply(3);
        var added = multiplied.Add(Money.Create(48.50m, "EGP"));

        Assert.Equal("EGP", price.Currency);
        Assert.Equal(751.50m, multiplied.Amount);
        Assert.Equal(800.00m, added.Amount);
    }

    [Fact]
    public void Money_Add_WithDifferentCurrency_ThrowsDomainException()
    {
        var egp = Money.Create(100m, "EGP");
        var usd = Money.Create(100m, "USD");

        var ex = Assert.Throws<DomainException>(() => egp.Add(usd));
        Assert.Equal("order.currency_mismatch", ex.Code);
    }

    [Theory]
    [InlineData(-0.01, "EGP")]
    [InlineData(10.123, "EGP")]
    [InlineData(10.00, "")]
    [InlineData(10.00, "EG")]
    [InlineData(10.00, "EGPP")]
    [InlineData(10.00, "E1P")]
    public void Money_Create_WithInvalidInput_ThrowsDomainException(decimal amount, string currency)
    {
        var ex = Assert.Throws<DomainException>(() => Money.Create(amount, currency));
        Assert.Equal("order.invalid_money", ex.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Quantity_Create_WithinRange_Succeeds(int value)
    {
        var q = Quantity.Create(value);
        Assert.Equal(value, q.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(101)]
    public void Quantity_Create_OutOfRange_ThrowsDomainException(int value)
    {
        var ex = Assert.Throws<DomainException>(() => Quantity.Create(value));
        Assert.Equal("order.invalid_quantity", ex.Code);
    }

    [Fact]
    public void Address_Create_TrimsAndUppercasesCountry()
    {
        var address = Address.Create("  Alice Smith ", " 123 Nile St ", " Cairo ", " eg ");

        Assert.Equal("Alice Smith", address.RecipientName);
        Assert.Equal("123 Nile St", address.Street);
        Assert.Equal("Cairo", address.City);
        Assert.Equal("EG", address.Country);
    }

    [Theory]
    [InlineData("", "123 St", "Cairo", "EG")]
    [InlineData("Alice", "", "Cairo", "EG")]
    [InlineData("Alice", "123 St", "", "EG")]
    [InlineData("Alice", "123 St", "Cairo", "")]
    [InlineData("Alice", "123 St", "Cairo", "EGY")]
    [InlineData("Alice", "123 St", "Cairo", "E1")]
    public void Address_Create_WithInvalidFields_ThrowsDomainException(
        string recipient,
        string street,
        string city,
        string country)
    {
        var ex = Assert.Throws<DomainException>(() =>
            Address.Create(recipient, street, city, country));

        Assert.Equal("order.invalid_address", ex.Code);
    }
}
