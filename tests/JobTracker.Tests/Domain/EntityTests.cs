using FluentAssertions;
using JobTracker.Domain.Common;
using Xunit;

namespace JobTracker.Tests.Domain;

/// <summary>
/// Covers <see cref="Entity"/> identity semantics. These matter beyond the Job aggregate: EF
/// uses them for change tracking, and the "empty id falls back to reference equality" branch is
/// what keeps two not-yet-persisted entities from comparing equal.
/// </summary>
public class EntityTests
{
    [Fact]
    public void Constructor_WithEmptyId_Throws()
    {
        var act = () => new TestEntity(Guid.Empty);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("id");
    }

    [Fact]
    public void Entities_OfTheSameTypeWithTheSameId_AreEqual()
    {
        var id = Guid.CreateVersion7();
        var left = new TestEntity(id);
        var right = new TestEntity(id);

        left.Should().Be(right);
        left.Equals((object)right).Should().BeTrue();
        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
        left.GetHashCode().Should().Be(right.GetHashCode());
    }

    [Fact]
    public void Entities_WithDifferentIds_AreNotEqual()
    {
        var left = new TestEntity(Guid.CreateVersion7());
        var right = new TestEntity(Guid.CreateVersion7());

        left.Should().NotBe(right);
        (left == right).Should().BeFalse();
    }

    /// <summary>A shared id across different types must not make two entities the same thing.</summary>
    [Fact]
    public void Entities_OfDifferentTypesWithTheSameId_AreNotEqual()
    {
        var id = Guid.CreateVersion7();
        var left = new TestEntity(id);
        var right = new OtherTestEntity(id);

        left.Equals(right).Should().BeFalse();
        right.Equals(left).Should().BeFalse();
    }

    /// <summary>
    /// The parameterless constructor (the one EF materialization uses) leaves Id empty. Two such
    /// entities are only equal to themselves, so a pair of unsaved entities never collapses into
    /// one.
    /// </summary>
    [Fact]
    public void Entities_WithEmptyIds_FallBackToReferenceEquality()
    {
        var left = new TestEntity();
        var right = new TestEntity();

        left.Id.Should().Be(Guid.Empty);
        left.Equals(right).Should().BeFalse();
        left.Equals(left).Should().BeTrue();
    }

    [Fact]
    public void Entity_IsNotEqualToNull()
    {
        var entity = new TestEntity(Guid.CreateVersion7());
        TestEntity? missing = null;
        object? nothing = null;

        // The object overload is asserted first on purpose: a preceding Equals(null) call
        // narrows the compiler's null-state for the receiver and makes the later statements
        // warn (CS8602). Keep this order.
        entity.Equals(nothing).Should().BeFalse();
        entity.Equals(null).Should().BeFalse();
        (entity == missing).Should().BeFalse();
        (entity != missing).Should().BeTrue();
    }

    [Fact]
    public void TwoNullEntities_AreEqual()
    {
        TestEntity? left = null;
        TestEntity? right = null;

        (left == right).Should().BeTrue();
        (left != right).Should().BeFalse();
    }

    [Fact]
    public void Equals_AgainstANonEntity_IsFalse()
    {
        var entity = new TestEntity(Guid.CreateVersion7());

        entity.Equals("not an entity").Should().BeFalse();
    }

    private sealed class TestEntity : Entity
    {
        public TestEntity(Guid id)
            : base(id)
        {
        }

        public TestEntity()
        {
        }
    }

    private sealed class OtherTestEntity : Entity
    {
        public OtherTestEntity(Guid id)
            : base(id)
        {
        }
    }
}
