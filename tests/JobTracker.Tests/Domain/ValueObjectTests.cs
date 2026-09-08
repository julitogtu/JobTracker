using FluentAssertions;
using JobTracker.Domain.Common;
using Xunit;

namespace JobTracker.Tests.Domain;

/// <summary>
/// Covers <see cref="ValueObject"/> structural equality directly, using a test double rather
/// than <see cref="AddressTests"/>' concrete Address, so the base-class rules (component order,
/// type check, null components) are pinned independently of any one value object.
/// </summary>
public class ValueObjectTests
{
    [Fact]
    public void ValueObjects_WithTheSameComponents_AreEqual()
    {
        var left = new TestValue("a", 1);
        var right = new TestValue("a", 1);

        left.Should().Be(right);
        left.Equals((object)right).Should().BeTrue();
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Theory]
    [InlineData("b", 1)]
    [InlineData("a", 2)]
    [InlineData("b", 2)]
    public void ValueObjects_WithDifferentComponents_AreNotEqual(string text, int number)
    {
        var left = new TestValue("a", 1);
        var right = new TestValue(text, number);

        left.Should().NotBe(right);
        (left == right).Should().BeFalse();
    }

    /// <summary>Equality is by component sequence, so ordering is part of the contract.</summary>
    [Fact]
    public void ValueObjects_WithTheSameComponentsInADifferentOrder_AreNotEqual()
    {
        var left = new TestValue("a", 1);
        var right = new ReversedTestValue("a", 1);

        left.Equals(right).Should().BeFalse();
    }

    [Fact]
    public void ValueObjects_OfDifferentTypes_AreNotEqual()
    {
        var left = new TestValue("a", 1);
        var right = new OtherTestValue("a", 1);

        left.Equals(right).Should().BeFalse();
        right.Equals(left).Should().BeFalse();
    }

    [Fact]
    public void ValueObjects_WithNullComponents_AreEqual()
    {
        var left = new TestValue(null, 1);
        var right = new TestValue(null, 1);

        left.Should().Be(right);
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void ValueObject_IsNotEqualToNull()
    {
        var value = new TestValue("a", 1);
        TestValue? missing = null;
        object? nothing = null;

        // The object overload is asserted first on purpose: a preceding Equals(null) call
        // narrows the compiler's null-state for the receiver and makes the later statements
        // warn (CS8602). Keep this order.
        value.Equals(nothing).Should().BeFalse();
        value.Equals(null).Should().BeFalse();
        (value == missing).Should().BeFalse();
        (value != missing).Should().BeTrue();
    }

    [Fact]
    public void TwoNullValueObjects_AreEqual()
    {
        TestValue? left = null;
        TestValue? right = null;

        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void Equals_AgainstANonValueObject_IsFalse()
    {
        var value = new TestValue("a", 1);

        value.Equals("not a value object").Should().BeFalse();
    }

    private sealed class TestValue(string? text, int number) : ValueObject
    {
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return text;
            yield return number;
        }
    }

    private sealed class ReversedTestValue(string? text, int number) : ValueObject
    {
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return number;
            yield return text;
        }
    }

    private sealed class OtherTestValue(string? text, int number) : ValueObject
    {
        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return text;
            yield return number;
        }
    }
}
