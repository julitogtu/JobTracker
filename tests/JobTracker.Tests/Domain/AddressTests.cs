using FluentAssertions;
using JobTracker.Domain.Jobs;
using Xunit;

namespace JobTracker.Tests.Domain;

/// <summary>
/// Covers the <see cref="Address"/> value object: field validation, coordinate ranges, and the
/// structural equality that <c>ValueObject</c> gives it. Address is owned by the Job aggregate
/// and flattened into <c>address_*</c> columns, so its equality is what EF change tracking uses
/// to decide whether the address was modified.
/// </summary>
public class AddressTests
{
    private const string Street = "123 Main St";
    private const string City = "Austin";
    private const string State = "TX";
    private const string ZipCode = "78701";
    private const decimal Latitude = 30.2672m;
    private const decimal Longitude = -97.7431m;

    [Fact]
    public void Constructor_WithValidInput_SetsEveryComponent()
    {
        var address = new Address(Street, City, State, ZipCode, Latitude, Longitude);

        address.Street.Should().Be(Street);
        address.City.Should().Be(City);
        address.State.Should().Be(State);
        address.ZipCode.Should().Be(ZipCode);
        address.Latitude.Should().Be(Latitude);
        address.Longitude.Should().Be(Longitude);
    }

    [Fact]
    public void Constructor_TrimsEveryTextField()
    {
        var address = new Address("  123 Main St  ", "  Austin  ", "  TX  ", "  78701  ", Latitude, Longitude);

        address.Street.Should().Be(Street);
        address.City.Should().Be(City);
        address.State.Should().Be(State);
        address.ZipCode.Should().Be(ZipCode);
    }

    [Theory]
    [InlineData("", City, State, ZipCode, "street")]
    [InlineData("   ", City, State, ZipCode, "street")]
    [InlineData(Street, "", State, ZipCode, "city")]
    [InlineData(Street, City, "", ZipCode, "state")]
    [InlineData(Street, City, State, "", "zipCode")]
    public void Constructor_WithBlankField_Throws(
        string street,
        string city,
        string state,
        string zipCode,
        string expectedParameter)
    {
        var act = () => new Address(street, city, state, zipCode, Latitude, Longitude);

        act.Should().Throw<ArgumentException>()
            .WithParameterName(expectedParameter);
    }

    [Theory]
    [InlineData(201, 100, 100, 20, "street")]
    [InlineData(200, 101, 100, 20, "city")]
    [InlineData(200, 100, 101, 20, "state")]
    [InlineData(200, 100, 100, 21, "zipCode")]
    public void Constructor_WithFieldOverMaxLength_Throws(
        int streetLength,
        int cityLength,
        int stateLength,
        int zipCodeLength,
        string expectedParameter)
    {
        var act = () => new Address(
            new string('a', streetLength),
            new string('a', cityLength),
            new string('a', stateLength),
            new string('a', zipCodeLength),
            Latitude,
            Longitude);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot exceed*")
            .WithParameterName(expectedParameter);
    }

    [Theory]
    [InlineData(-90.0001)]
    [InlineData(90.0001)]
    [InlineData(1000)]
    public void Constructor_WithLatitudeOutOfRange_Throws(decimal latitude)
    {
        var act = () => new Address(Street, City, State, ZipCode, latitude, Longitude);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("latitude");
    }

    [Theory]
    [InlineData(-180.0001)]
    [InlineData(180.0001)]
    [InlineData(1000)]
    public void Constructor_WithLongitudeOutOfRange_Throws(decimal longitude)
    {
        var act = () => new Address(Street, City, State, ZipCode, Latitude, longitude);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("longitude");
    }

    [Theory]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(0, 0)]
    public void Constructor_AcceptsTheCoordinateBoundaries(decimal latitude, decimal longitude)
    {
        var address = new Address(Street, City, State, ZipCode, latitude, longitude);

        address.Latitude.Should().Be(latitude);
        address.Longitude.Should().Be(longitude);
    }

    [Fact]
    public void Addresses_WithTheSameComponents_AreEqual()
    {
        var left = new Address(Street, City, State, ZipCode, Latitude, Longitude);
        var right = new Address(Street, City, State, ZipCode, Latitude, Longitude);

        left.Should().Be(right);
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Addresses_DifferingByOneComponent_AreNotEqual()
    {
        var left = new Address(Street, City, State, ZipCode, Latitude, Longitude);
        var right = new Address(Street, "Dallas", State, ZipCode, Latitude, Longitude);

        left.Should().NotBe(right);
        (left == right).Should().BeFalse();
    }

    /// <summary>Trimming happens before equality, so padded input is the same address.</summary>
    [Fact]
    public void Addresses_DifferingOnlyByWhitespace_AreEqual()
    {
        var left = new Address(Street, City, State, ZipCode, Latitude, Longitude);
        var right = new Address("  123 Main St ", " Austin ", " TX ", " 78701 ", Latitude, Longitude);

        left.Should().Be(right);
    }

    [Fact]
    public void Address_IsNotEqualToNull()
    {
        var address = new Address(Street, City, State, ZipCode, Latitude, Longitude);

        address.Equals(null).Should().BeFalse();
        (address == null).Should().BeFalse();
        (address != null).Should().BeTrue();
    }
}
