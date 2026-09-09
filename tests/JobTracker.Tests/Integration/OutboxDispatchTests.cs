using FluentAssertions;
using JobTracker.Application.Common.Messaging;
using JobTracker.BackgroundJob.Billing;
using JobTracker.BackgroundJob.Notifications;
using JobTracker.BackgroundJob.Outbox;
using JobTracker.Domain.Jobs;
using JobTracker.Persistence.Outbox;
using JobTracker.Tests.BackgroundJob;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobTracker.Tests.Integration;

public sealed class OutboxDispatchTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly RecordingBackgroundJobClient hangfire = new();

    [Fact]
    public async Task CompletingAJob_QueuesInvoiceGenerationAndCustomerNotification()
    {
        var job = await CompleteAJobAsync();

        var claimed = await Drain();

        claimed.Should().BeGreaterThanOrEqualTo(1);

        var invoice = InvoiceFor(job.Id);
        var notification = NotificationFor(job.Id);

        invoice.JobId.Should().Be(job.Id);
        invoice.OrganizationId.Should().Be(OrganizationId);
        invoice.CustomerId.Should().Be(job.CustomerId);

        notification.JobId.Should().Be(job.Id);
        notification.CustomerId.Should().Be(job.CustomerId);
    }

    [Fact]
    public async Task ADispatchedMessage_IsMarkedProcessed()
    {
        var job = await CompleteAJobAsync();

        await Drain();

        var row = (await ReadOutboxAsync(job.Id)).Single();

        row.ProcessedOnUtc.Should().Be(Clock.GetUtcNow());
        row.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task ASecondDrain_DoesNotDispatchTheSameMessageAgain()
    {
        await CompleteAJobAsync();

        await Drain();
        var enqueuedAfterFirstDrain = hangfire.Enqueued.Count;

        await Drain();

        hangfire.Enqueued.Should().HaveCount(enqueuedAfterFirstDrain);
    }

    [Fact]
    public async Task TheCorrelationIdOfTheCompletingRequest_ReachesBothModules()
    {
        Correlation.CorrelationId = "corr-completing-request";

        var job = await CompleteAJobAsync();
        await Drain();

        InvoiceFor(job.Id).CorrelationId.Should().Be("corr-completing-request");
        NotificationFor(job.Id).CorrelationId.Should().Be("corr-completing-request");
    }

    [Fact]
    public async Task AFailedDispatch_LeavesTheRowPendingAndBacksOff()
    {
        var job = await CompleteAJobAsync();

        await ProcessorWith(new ThrowingPublisher()).ExecuteAsync(batchSize: 10, cancellationToken: Ct);

        var row = (await ReadOutboxAsync(job.Id)).Single();

        row.ProcessedOnUtc.Should().BeNull();
        row.RetryCount.Should().Be(1);
        row.NextAttemptOnUtc.Should().BeAfter(Clock.GetUtcNow());
    }

    private async Task<Job> CompleteAJobAsync()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);

        Clock.Advance(TimeSpan.FromHours(2));
        tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        return job;
    }

    private GenerateInvoiceCommand InvoiceFor(Guid jobId) =>
        hangfire.ArgumentsFor<InvoiceGenerationJob, GenerateInvoiceCommand>()
            .Single(command => command.JobId == jobId);

    private NotifyCustomerOfCompletionCommand NotificationFor(Guid jobId) =>
        hangfire.ArgumentsFor<CustomerNotificationJob, NotifyCustomerOfCompletionCommand>()
            .Single(command => command.JobId == jobId);

    private Task<int> Drain() =>
        ProcessorWith(new HangfireOutboxDispatcher(hangfire, NullLogger<HangfireOutboxDispatcher>.Instance))
            .ExecuteAsync(batchSize: 10, cancellationToken: Ct);

    private OutboxMessageProcessor ProcessorWith(IOutboxMessagePublisher publisher) =>
        new(Db, publisher, Clock, NullLogger<OutboxMessageProcessor>.Instance);

    private sealed class ThrowingPublisher : IOutboxMessagePublisher
    {
        public Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the broker is down");
    }
}
