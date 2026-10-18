using ProductService.Domain.Abstractions;
using ProductService.Domain.Products;

namespace ProductService.UnitTests.Domain;

public sealed class SkuTests
{
    [Fact]
    public void Create_WithValidInput_TrimsAndUppercases()
    {
        var sku = Sku.Create("  kb-mech-01  ");

        Assert.Equal("KB-MECH-01", sku.Value);
        Assert.Equal(Sku.Create("KB-MECH-01"), sku);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WhenEmpty_ThrowsDomainExceptionWithSkuEmptyCode(string invalid)
    {
        var ex = Assert.Throws<DomainException>(() => Sku.Create(invalid));
        Assert.Equal("sku.empty", ex.Code);
    }

    [Fact]
    public void Create_WhenExceedsMaxLength_ThrowsDomainExceptionWithSkuTooLongCode()
    {
        var tooLong = new string('A', Sku.MaxLength + 1);

        var ex = Assert.Throws<DomainException>(() => Sku.Create(tooLong));
        Assert.Equal("sku.too_long", ex.Code);
    }
}
