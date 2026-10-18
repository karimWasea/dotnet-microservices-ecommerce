using UserService.Domain.Abstractions;

namespace UserService.Domain.Users;

public sealed record PasswordHash
{
    public const int MinLength = 20;

    public string Value { get; }

    private PasswordHash(string value)
    {
        Value = value;
    }

    public static PasswordHash Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Password hash is required.", "user.invalid_password_hash");
        }

        var trimmed = value.Trim();
        if (trimmed.Length < MinLength)
        {
            throw new DomainException("Password hash value is invalid or too short to be a valid hash.", "user.invalid_password_hash");
        }

        return new PasswordHash(trimmed);
    }

    // Never return the raw hash string when formatted or logged.
    public override string ToString() => "***REDACTED***";
}
