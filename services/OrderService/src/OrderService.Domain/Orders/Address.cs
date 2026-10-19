using OrderService.Domain.Abstractions;

namespace OrderService.Domain.Orders;

public sealed record Address
{
    public const int MaxRecipientNameLength = 100;
    public const int MaxStreetLength = 200;
    public const int MaxCityLength = 100;
    public const int CountryLength = 2;

    public string RecipientName { get; }
    public string Street { get; }
    public string City { get; }
    public string Country { get; }

    private Address(
        string recipientName,
        string street,
        string city,
        string country)
    {
        RecipientName = recipientName;
        Street = street;
        City = city;
        Country = country;
    }

    public static Address Create(
        string? recipientName,
        string? street,
        string? city,
        string? country)
    {
        var normalizedRecipient = ValidateField(recipientName, "RecipientName", MaxRecipientNameLength);
        var normalizedStreet = ValidateField(street, "Street", MaxStreetLength);
        var normalizedCity = ValidateField(city, "City", MaxCityLength);

        if (string.IsNullOrWhiteSpace(country))
        {
            throw new DomainException("Country is required.", "order.invalid_address");
        }

        var normalizedCountry = country.Trim().ToUpperInvariant();
        if (normalizedCountry.Length != CountryLength ||
            !normalizedCountry.All(ch => ch is >= 'A' and <= 'Z'))
        {
            throw new DomainException("Country must be a 2-letter uppercase ISO country code.", "order.invalid_address");
        }

        return new Address(normalizedRecipient, normalizedStreet, normalizedCity, normalizedCountry);
    }

    private static string ValidateField(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{fieldName} is required.", "order.invalid_address");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{fieldName} must not exceed {maxLength} characters.", "order.invalid_address");
        }

        return trimmed;
    }
}
