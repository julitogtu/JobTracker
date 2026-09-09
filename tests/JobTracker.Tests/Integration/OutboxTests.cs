using System.Text.Json;
using FluentAssertions;
using JobTracker.Domain.Enums;
using JobTracker.Jobs.IntegrationEvents;
using Xunit;

namespace JobTracker.Tests.Integration;

/// <summary>
/// <c>InsertOutboxMessagesInterceptor</c> is a SaveChanges interceptor, so the only honest way to
/// test it is to actually save: the row and the aggregate mutation have to land in the same
/// transaction, and that is a database fact, not an object-graph fact.
///
/// Note what this suite does NOT claim: nothing drains the outbox. <c>OutboxMessageProcessor</c>
/// is registered but never invoked, and <c>OutboxMessagePublisher</c> is a no-op stub, so a row
/// written here is a row that stays. These tests pin the write side only.
/// </summary>
public sealed class OutboxTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CompletingAJob_WritesOneOutboxMessage()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        Clock.Advance(TimeSpan.FromHours(2));
        tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
        await SaveChangesAsync();

        var rows = await ReadOutboxAsync(job.Id);

        rows.Should().ContainSingle();
        rows[0].Type.Should().Be(typeof(JobCompletedIntegrationEvent).FullName);
        rows[0].OccurredOnUtc.Should().Be(Clock.GetUtcNow());
        rows[0].ProcessedOnUtc.Should().BeNull();
        rows[0].RetryCount.Should().Be(0);
        rows[0].NextAttemptOnUtc.Should().Be(rows[0].OccurredOnUtc);
    }

    [Fact]
    public async Task OutboxMessage_CarriesTheIntegrationEventPayload()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        Clock.Advance(TimeSpan.FromHours(2));
        tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
        await SaveChangesAsync();

        var row = (await ReadOutboxAsync(job.Id)).Single();

        var integrationEvent = JsonSerializer.Deserialize<JobCompletedIntegrationEvent>(
            row.Content,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        integrationEvent.Should().NotBeNull();
        integrationEvent!.JobId.Should().Be(job.Id);
        integrationEvent.OrganizationId.Should().Be(OrganizationId);
        integrationEvent.CustomerId.Should().Be(job.CustomerId);
        integrationEvent.CompletedAtUtc.Should().Be(Clock.GetUtcNow());
        integrationEvent.EventId.Should().Be(row.Id);
    }

    /// <summary>
    /// Content is a <c>jsonb</c> column serialized with web defaults, so the payload is queryable
    /// by camelCase key — which is what the outbox reader and every ad-hoc SQL query rely on.
    /// </summary>
    [Fact]
    public async Task OutboxMessage_IsStoredAsQueryableJsonb()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);
        tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
        await SaveChangesAsync();

        // ReadOutboxAsync itself filters on content ->> 'jobId', so a hit proves the shape.
        (await ReadOutboxAsync(job.Id)).Should().ContainSingle();

        var byOrganization = await CountAsync(
            "outbox_messages",
            "content ->> 'organizationId' = @organizationId",
            ("organizationId", OrganizationId.ToString()));

        byOrganization.Should().Be(1);
    }

    /// <summary>
    /// The interceptor removes the event it mapped. Without that, a second SaveChanges on the
    /// same tracked aggregate would write the row again.
    /// </summary>
    [Fact]
    public async Task CompletingAJob_RemovesTheMappedDomainEvent()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
        tracked.DomainEvents.Should().ContainSingle();

        await SaveChangesAsync();

        tracked.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task SavingTwice_DoesNotWriteTheMessageTwice()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
        await SaveChangesAsync();
        await SaveChangesAsync();

        (await ReadOutboxAsync(job.Id)).Should().ContainSingle();
    }

    /// <summary>
    /// Only <c>JobCompletedDomainEvent</c> is mapped. <c>JobCreatedDomainEvent</c> is raised and
    /// then dropped on save — a documented gap, pinned here so it changes deliberately rather
    /// than by accident.
    /// </summary>
    [Fact]
    public async Task CreatingAJob_WritesNoOutboxMessage()
    {
        var job = await SaveDraftAsync();

        (await ReadOutboxAsync(job.Id)).Should().BeEmpty();
    }

    /// <summary>
    /// <c>JobCancelledDomainEvent</c> is raised but unmapped — the same documented gap as
    /// creation. If cancellation ever needs to leave the aggregate, the interceptor is what
    /// needs extending.
    /// </summary>
    [Fact]
    public async Task CancellingAJob_WritesNoOutboxMessage()
    {
        var job = await SaveScheduledAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        tracked!.Cancel(Clock.GetUtcNow(), "Customer called it off.");
        await SaveChangesAsync();

        (await ReadOutboxAsync(job.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task StartingAJob_WritesNoOutboxMessage()
    {
        var job = await SaveScheduledAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        tracked!.Start(Clock.GetUtcNow());
        await SaveChangesAsync();

        (await ReadOutboxAsync(job.Id)).Should().BeEmpty();
    }

    /// <summary>
    /// The row is written by the same SaveChanges that mutates the job, so a failure after the
    /// interceptor runs must take both with it. Rolling back an explicit transaction is the
    /// cheapest way to observe that they are genuinely one unit.
    /// </summary>
    [Fact]
    public async Task OutboxMessage_AndTheJobUpdate_ShareOneTransaction()
    {
        var job = await SaveInProgressAsync();

        await using (var transaction = await Db.Database.BeginTransactionAsync(Ct))
        {
            var tracked = await GetJobAsync(OrganizationId, job.Id);
            tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
            await SaveChangesAsync();

            (await ReadOutboxAsync(job.Id)).Should()
                .ContainSingle("the row is visible inside the transaction");

            await transaction.RollbackAsync(Ct);
        }

        Db.ChangeTracker.Clear();

        (await ReadOutboxAsync(job.Id)).Should().BeEmpty("the rollback took the outbox row with it");
        (await GetJobAsync(OrganizationId, job.Id))!.Status
            .Should().Be(JobStatus.InProgress, "and the job update with it");
    }

    [Fact]
    public async Task CompletingTwoJobs_WritesOneMessageEach()
    {
        var first = await SaveInProgressAsync();
        var second = await SaveInProgressAsync();

        var trackedFirst = await GetJobAsync(OrganizationId, first.Id);
        var trackedSecond = await GetJobAsync(OrganizationId, second.Id);

        trackedFirst!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/a.png");
        trackedSecond!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/b.png");
        await SaveChangesAsync();

        (await ReadOutboxAsync(first.Id)).Should().ContainSingle();
        (await ReadOutboxAsync(second.Id)).Should().ContainSingle();
    }
}
