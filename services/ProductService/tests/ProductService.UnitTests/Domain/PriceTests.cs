using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.UnitTests.Domain;

public sealed class PriceTests
{
    [Theory]
    [InlineData(0.01)]
    [InlineData(19.9)]
    [InlineData(129.99)]
    public void Create_WithValidAmount_SucceedsAndSupportsValueEquality(decimal amount)
    {
        var price1 = Price.Create(amount);
        var price2 = Price.Create(amount);

        Assert.Equal(amount, price1.Amount);
        Assert.Equal(price1, price2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Create_WhenNotPositive_ThrowsDomainExceptionWithPriceNotPositiveCode(decimal invalid)
    {
        var ex = Assert.Throws<DomainException>(() => Price.Create(invalid));
        Assert.Equal("price.not_positive", ex.Code);
    }

    [Theory]
    [InlineData(10.123)]
    [InlineData(0.001)]
    [InlineData(99.999)]
    public void Create_WhenMoreThanTwoDecimalPlaces_ThrowsDomainExceptionWithoutSilentRounding(decimal invalid)
    {
        var ex = Assert.Throws<DomainException>(() => Price.Create(invalid));
        Assert.Equal("price.too_many_decimals", ex.Code);
    }
}
