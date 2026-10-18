using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using UserService.Application.Abstractions;
using UserService.Domain.Users;

namespace UserService.Infrastructure.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    public const int DefaultIterationCount = 220_000;
    public const int MinimumIterationCount = 210_000;

    private static readonly object DummyUser = new();
    private readonly PasswordHasher<object> _passwordHasher;

    public Pbkdf2PasswordHasher(int iterationCount = DefaultIterationCount)
    {
        var effectiveIterations = Math.Max(iterationCount, MinimumIterationCount);
        var options = Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = effectiveIterations
        });

        _passwordHasher = new PasswordHasher<object>(options);
    }

    public string Hash(string rawPassword)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawPassword);
        return _passwordHasher.HashPassword(DummyUser, rawPassword);
    }

    public bool Verify(string rawPassword, PasswordHash passwordHash)
    {
        if (string.IsNullOrEmpty(rawPassword) || passwordHash is null)
        {
            return false;
        }

        try
        {
            var result = _passwordHasher.VerifyHashedPassword(DummyUser, passwordHash.Value, rawPassword);
            return result is PasswordVerificationResult.Success
                or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
