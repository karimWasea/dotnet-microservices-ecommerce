using UserService.Domain.Abstractions;
using UserService.Domain.Users;

namespace UserService.UnitTests.Domain;

public sealed class PasswordHashTests
{
    [Fact]
    public void Create_WithValidHash_StoresTrimmedValueAndRedactsToString()
    {
        const string validHash = "AQAAAAIAAYagAAAAEHashValueWithSufficientLength12345==";

        var passwordHash = PasswordHash.Create($"  {validHash}  ");

        Assert.Equal(validHash, passwordHash.Value);
        Assert.Equal("***REDACTED***", passwordHash.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("short_hash_123")]
    public void Create_WithInvalidOrShortValue_ThrowsDomainException(string? input)
    {
        var ex = Assert.Throws<DomainException>(() => PasswordHash.Create(input));

        Assert.Equal("user.invalid_password_hash", ex.Code);
    }
}
