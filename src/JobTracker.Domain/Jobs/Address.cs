using JobTracker.Domain.Common;

namespace JobTracker.Domain.Jobs;

public sealed class Address : ValueObject
{
    private Address() { }

    public Address(string street, string city, string state, string zipCode, decimal latitude, decimal longitude)
    {
        Street = Required(street, nameof(street), 200);
        City = Required(city, nameof(city), 100);
        State = Required(state, nameof(state), 100);
        ZipCode = Required(zipCode, nameof(zipCode), 20);

        if (latitude is < -90m or > 90m)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        }

        if (longitude is < -180m or > 180m)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public string Street { get; private init; } = string.Empty;

    public string City { get; private init; } = string.Empty;

    public string State { get; private init; } = string.Empty;

    public string ZipCode { get; private init; } = string.Empty;

    public decimal Latitude { get; private init; }

    public decimal Longitude { get; private init; }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Street;
        yield return City;
        yield return State;
        yield return ZipCode;
        yield return Latitude;
        yield return Longitude;
    }

    private static string Required(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName);
        }

        return normalized;
    }
}
