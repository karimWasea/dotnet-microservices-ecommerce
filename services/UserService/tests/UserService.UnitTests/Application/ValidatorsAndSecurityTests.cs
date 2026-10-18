using FluentValidation;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using UserService.Application.Behaviors;
using UserService.Application.Users.Commands.ChangePassword;
using UserService.Application.Users.Commands.RegisterUser;
using UserService.Application.Users.Queries.ListUsers;
using UserService.Domain.Users;
using UserService.Infrastructure.Security;

namespace UserService.UnitTests.Application;

public sealed class ValidatorsAndSecurityTests
{
    [Theory]
    [InlineData("short1!A")]            // < 12 chars
    [InlineData("alllowercase123!")]    // no uppercase
    [InlineData("ALLUPPERCASE123!")]    // no lowercase
    [InlineData("NoDigitsHere!!!!")]    // no digit
    [InlineData("NoSymbolsHere123")]    // no symbol
    public async Task RegisterUserCommandValidator_WithWeakPassword_FailsValidation(string weakPassword)
    {
        var validator = new RegisterUserCommandValidator();
        var command = new RegisterUserCommand("alice@example.com", weakPassword, "Alice");

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RegisterUserCommand.Password));
    }

    [Fact]
    public async Task ChangePasswordCommandValidator_WhenNewMatchesCurrent_FailsValidation()
    {
        var validator = new ChangePasswordCommandValidator();
        var command = new ChangePasswordCommand(
            Guid.CreateVersion7(),
            "SamePassword@123!",
            "SamePassword@123!");

        var result = await validator.ValidateAsync(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public async Task ListUsersQueryValidator_WithInvalidRoleOrPage_FailsValidation()
    {
        var validator = new ListUsersQueryValidator();
        var query = new ListUsersQuery(Role: "SuperUser", Page: 0, PageSize: 500);

        var result = await validator.ValidateAsync(query);

        Assert.False(result.IsValid);
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public async Task ValidationBehavior_WhenValidationFails_ThrowsValidationException()
    {
        var validators = new IValidator<RegisterUserCommand>[] { new RegisterUserCommandValidator() };
        var behavior = new ValidationBehavior<RegisterUserCommand, string>(validators);
        var invalidCommand = new RegisterUserCommand("bad-email", "short", "");

        await Assert.ThrowsAsync<ValidationException>(() =>
            behavior.Handle(
                invalidCommand,
                _ => Task.FromResult("ok"),
                CancellationToken.None));
    }

    [Fact]
    public void Pbkdf2PasswordHasher_HashesAndVerifiesPasswordCorrectly()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var rawHash = hasher.Hash("Customer@123456!");
        var passwordHash = PasswordHash.Create(rawHash);

        Assert.True(hasher.Verify("Customer@123456!", passwordHash));
        Assert.False(hasher.Verify("WrongPassword@123!", passwordHash));
    }

    [Fact]
    public async Task JwtTokenService_CreatesValidRs256AccessTokenAndVerifiableWithJwksKey()
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "ecommerce.userservice",
            Audience = "ecommerce.api",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7,
            KeyId = "test-kid-1",
            PrivateKeyPath = string.Empty
        });

        using var rsaKeyMaterial = new RsaKeyMaterial(jwtOptions);
        var tokenService = new JwtTokenService(jwtOptions, rsaKeyMaterial);

        var now = DateTimeOffset.UtcNow;
        var user = User.Register(
            Email.Create("alice@ecommerce.local"),
            "Alice",
            PasswordHash.Create("AQAAAAIAAYagAAAAEValidHashedPasswordString1234567890=="),
            Role.Customer,
            now);

        var accessResult = tokenService.CreateAccessToken(user, now);
        var jwks = rsaKeyMaterial.GetJwksDocument();

        Assert.Single(jwks.keys);
        Assert.Equal("RSA", jwks.keys[0].kty);
        Assert.Equal("RS256", jwks.keys[0].alg);
        Assert.Equal("test-kid-1", jwks.keys[0].kid);

        var handler = new JsonWebTokenHandler();
        var validationResult = await handler.ValidateTokenAsync(
            accessResult.AccessToken,
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "ecommerce.userservice",
                ValidateAudience = true,
                ValidAudience = "ecommerce.api",
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = rsaKeyMaterial.ValidationKey,
                ValidateLifetime = true
            });

        Assert.True(validationResult.IsValid);
        Assert.Equal(user.Id.ToString(), validationResult.Claims["sub"]?.ToString());
        Assert.Equal("alice@ecommerce.local", validationResult.Claims["email"]?.ToString());
        Assert.Equal("Customer", validationResult.Claims["role"]?.ToString());
    }
}
