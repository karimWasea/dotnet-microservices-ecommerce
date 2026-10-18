using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.UnitTests.Domain;

public sealed class EmailTests
{
    [Fact]
    public void Create_WithValidEmail_TrimsAndLowercases()
    {
        var email = Email.Create("  Alice.Smith@Example.COM  ");

        Assert.Equal("alice.smith@example.com", email.Value);
        Assert.Equal("alice.smith@example.com", email.ToString());
    }

    [Fact]
    public void Equals_WithSameNormalizedValue_ReturnsTrue()
    {
        var first = Email.Create("USER@example.com");
        var second = Email.Create("user@EXAMPLE.com");

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plainaddress")]
    [InlineData("@example.com")]
    [InlineData("alice@")]
    [InlineData("alice@@example.com")]
    [InlineData("alice@example")]
    [InlineData("alice@.example.com")]
    [InlineData("alice@example.com.")]
    [InlineData("alice@example..com")]
    [InlineData("alice smith@example.com")]
    public void Create_WithInvalidFormat_ThrowsDomainException(string? input)
    {
        var ex = Assert.Throws<DomainException>(() => Email.Create(input));

        Assert.Equal("user.invalid_email", ex.Code);
    }

    [Fact]
    public void Create_ExceedingMaxLength_ThrowsDomainException()
    {
        var longLocal = new string('a', 245);
        var tooLongEmail = $"{longLocal}@example.com"; // 257 chars > 254

        var ex = Assert.Throws<DomainException>(() => Email.Create(tooLongEmail));

        Assert.Equal("user.invalid_email", ex.Code);
    }
}
