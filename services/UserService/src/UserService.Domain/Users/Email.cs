using UserService.Domain.Abstractions;

namespace UserService.Domain.Users;

public sealed record Email
{
    public const int MaxLength = 254;

    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Email is required.", "user.invalid_email");
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
        {
            throw new DomainException($"Email must not exceed {MaxLength} characters.", "user.invalid_email");
        }

        if (normalized.Any(char.IsWhiteSpace))
        {
            throw new DomainException("Email cannot contain whitespace.", "user.invalid_email");
        }

        var atIndex = normalized.IndexOf('@');
        if (atIndex <= 0 || atIndex != normalized.LastIndexOf('@') || atIndex == normalized.Length - 1)
        {
            throw new DomainException("Email must contain a single '@' with local and domain parts.", "user.invalid_email");
        }

        var domainPart = normalized[(atIndex + 1)..];
        var dotIndex = domainPart.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == domainPart.Length - 1 || domainPart.StartsWith('.') || domainPart.EndsWith('.') || domainPart.Contains(".."))
        {
            throw new DomainException("Email domain must contain a valid dot-separated host.", "user.invalid_email");
        }

        return new Email(normalized);
    }

    public override string ToString() => Value;
}
