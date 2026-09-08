using FluentAssertions;
using JobTracker.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace JobTracker.Tests.Integration;

/// <summary>
/// Mapping fidelity: what the aggregate writes is what lands in the table, and what comes back
/// is the same aggregate. Assertions go against real columns wherever the mapping is the thing
/// under test — a round-trip through EF would happily hide a wrong column name or a lost
/// conversion.
/// </summary>
public sealed class JobPersistenceTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Job_RoundTripsThroughTheDatabase()
    {
        var saved = await SaveScheduledAsync();

        var loaded = await GetJobAsync(OrganizationId, saved.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(saved.Id);
        loaded.Title.Should().Be(saved.Title);
        loaded.Description.Should().Be(saved.Description);
        loaded.Status.Should().Be(JobStatus.Scheduled);
        loaded.OrganizationId.Should().Be(OrganizationId);
        loaded.CustomerId.Should().Be(saved.CustomerId);
        loaded.AssigneeId.Should().Be(saved.AssigneeId);
        loaded.ScheduledDateUtc.Should().Be(saved.ScheduledDateUtc);
        loaded.CreatedAtUtc.Should().Be(saved.CreatedAtUtc);
        loaded.UpdatedAtUtc.Should().Be(saved.UpdatedAtUtc);
    }

    [Fact]
    public async Task Address_IsFlattenedIntoAddressColumns()
    {
        var job = await SaveDraftAsync();

        (await ReadJobColumnAsync<string>(job.Id, "address_street")).Should().Be("123 Main St");
        (await ReadJobColumnAsync<string>(job.Id, "address_city")).Should().Be("Austin");
        (await ReadJobColumnAsync<string>(job.Id, "address_state")).Should().Be("TX");
        (await ReadJobColumnAsync<string>(job.Id, "address_zip_code")).Should().Be("78701");
    }

    [Fact]
    public async Task Address_RoundTripsAsAValueObject()
    {
        var job = await SaveDraftAsync();

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        loaded!.Address.Should().Be(AnAddress());
    }

    /// <summary>Coordinates are mapped with <c>HasPrecision(9, 6)</c>.</summary>
    [Fact]
    public async Task Coordinates_KeepSixDecimalPlaces()
    {
        var job = await SaveDraftAsync();

        (await ReadJobColumnAsync<decimal>(job.Id, "address_latitude")).Should().Be(30.267200m);
        (await ReadJobColumnAsync<decimal>(job.Id, "address_longitude")).Should().Be(-97.743100m);
    }

    /// <summary>
    /// <c>JobStatus</c> is stored as a string, not the underlying int. The React app reads the
    /// enum as a NUMBER over HTTP because no JsonStringEnumConverter is registered — these are
    /// two separate decisions, and this pins the database half.
    /// </summary>
    [Fact]
    public async Task Status_IsStoredAsAString()
    {
        var draft = await SaveDraftAsync();
        var inProgress = await SaveInProgressAsync();

        (await ReadJobColumnAsync<string>(draft.Id, "status")).Should().Be("Draft");
        (await ReadJobColumnAsync<string>(inProgress.Id, "status")).Should().Be("InProgress");
    }

    /// <summary>Ids are generated in the handler with <c>Guid.CreateVersion7()</c>, never by the database.</summary>
    [Fact]
    public async Task Id_IsTheOneTheApplicationSupplied()
    {
        var job = NewDraft();
        var expectedId = job.Id;

        await SaveAsync(job);

        (await ReadJobColumnAsync<Guid>(expectedId, "id")).Should().Be(expectedId);
    }

    [Fact]
    public async Task Timestamps_ComeBackAsUtc()
    {
        var job = await SaveInProgressAsync();

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        loaded!.CreatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        loaded.UpdatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        loaded.ScheduledDateUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        loaded.StartedAtUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task NullableTimestamps_StayNull()
    {
        var job = await SaveDraftAsync();

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        loaded!.ScheduledDateUtc.Should().BeNull();
        loaded.StartedAtUtc.Should().BeNull();
        loaded.CompletedAtUtc.Should().BeNull();
        loaded.CancelledAtUtc.Should().BeNull();
        loaded.SignatureUrl.Should().BeNull();
        loaded.CancellationReason.Should().BeNull();
    }

    [Fact]
    public async Task CompletedJob_PersistsItsSignatureAndTimestamps()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        Clock.Advance(TimeSpan.FromHours(3));
        tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        loaded!.Status.Should().Be(JobStatus.Completed);
        loaded.SignatureUrl.Should().Be("https://example.com/signatures/abc.png");
        loaded.CompletedAtUtc.Should().Be(Clock.GetUtcNow());
        (await ReadJobColumnAsync<string>(job.Id, "status")).Should().Be("Completed");
    }

    [Fact]
    public async Task CancelledJob_PersistsItsReason()
    {
        var job = await SaveScheduledAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        tracked!.Cancel(Clock.GetUtcNow(), "Customer called it off.");
        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        loaded!.Status.Should().Be(JobStatus.Cancelled);
        loaded.CancellationReason.Should().Be("Customer called it off.");
        loaded.CancelledAtUtc.Should().Be(Clock.GetUtcNow());
    }

    /* ---------------------------------------------------------------------------------------
     * Photos
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task Photos_AreSavedWithTheAggregate()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        tracked!.AddPhoto(Guid.CreateVersion7(), "https://example.com/photos/a.jpg", Clock.GetUtcNow(), "Before");
        tracked.AddPhoto(Guid.CreateVersion7(), "https://example.com/photos/b.jpg", Clock.GetUtcNow(), null);
        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        loaded!.Photos.Should().HaveCount(2);
        loaded.Photos.Should().ContainSingle(photo => photo.Caption == "Before");
        loaded.Photos.Should().ContainSingle(photo => photo.Caption == null);
        loaded.Photos.Should().OnlyContain(photo => photo.JobId == job.Id);
    }

    /// <summary>
    /// <c>GetByIdAsync</c> uses <c>Include(job =&gt; job.Photos)</c>: the aggregate always loads
    /// whole, so a handler never sees a partially populated Job.
    /// </summary>
    [Fact]
    public async Task Photos_LoadEagerlyWithTheJob()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);
        tracked!.AddPhoto(Guid.CreateVersion7(), "https://example.com/photos/a.jpg", Clock.GetUtcNow(), null);
        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        // No lazy loading is configured, so a populated collection proves the Include ran.
        loaded!.Photos.Should().ContainSingle();
    }

    [Fact]
    public async Task Photos_AreDeletedWithTheJob()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);
        tracked!.AddPhoto(Guid.CreateVersion7(), "https://example.com/photos/a.jpg", Clock.GetUtcNow(), null);
        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var toRemove = await Db.Jobs.SingleAsync(candidate => candidate.Id == job.Id, Ct);
        Db.Jobs.Remove(toRemove);
        await SaveChangesAsync();

        (await CountAsync("job_photos", "job_id = @jobId", ("jobId", job.Id))).Should().Be(0);
    }

    /* ---------------------------------------------------------------------------------------
     * Tenancy and change tracking
     * ------------------------------------------------------------------------------------ */

    /// <summary>
    /// OrganizationId is the only tenant boundary and there is no authentication behind it, so
    /// a query that forgets it leaks across organizations. This is the repository-level guard.
    /// </summary>
    [Fact]
    public async Task GetById_DoesNotReturnAJobFromAnotherOrganization()
    {
        var otherOrganizationId = Guid.CreateVersion7();
        var job = await SaveDraftAsync(organizationId: otherOrganizationId);

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        loaded.Should().BeNull();

        // Present under its own organization, so the null above is scoping, not a missing row.
        (await GetJobAsync(otherOrganizationId, job.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task GetById_ReturnsNullForAnUnknownJob()
    {
        (await GetJobAsync(OrganizationId, Guid.CreateVersion7())).Should().BeNull();
    }

    /// <summary><c>builder.Ignore(job =&gt; job.DomainEvents)</c> — events never reach a column.</summary>
    [Fact]
    public async Task DomainEvents_AreNotPersisted()
    {
        var job = await SaveDraftAsync();

        var loaded = await GetJobAsync(OrganizationId, job.Id);

        loaded!.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task AddAsync_WithNull_Throws()
    {
        var act = async () => await Jobs.AddAsync(null!, Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
