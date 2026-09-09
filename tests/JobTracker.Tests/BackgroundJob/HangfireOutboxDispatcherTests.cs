using System.Text.Json;
using FluentAssertions;
using Hangfire.States;
using JobTracker.Application.Common.Messaging;
using JobTracker.BackgroundJob.Billing;
using JobTracker.BackgroundJob.Notifications;
using JobTracker.BackgroundJob.Outbox;
using JobTracker.Jobs.IntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobTracker.Tests.BackgroundJob;

public sealed class HangfireOutboxDispatcherTests
{
    private static readonly DateTimeOffset CompletedAt = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private readonly RecordingBackgroundJobClient client = new();

    private HangfireOutboxDispatcher Dispatcher =>
        new(client, NullLogger<HangfireOutboxDispatcher>.Instance);

    [Fact]
    public async Task AJobCompletedMessage_EnqueuesInvoiceGenerationAndCustomerNotification()
    {
        var integrationEvent = AnEvent();

        await Dispatcher.PublishAsync(AnEnvelope(integrationEvent), TestContext.Current.CancellationToken);

        client.Enqueued.Select(entry => entry.Job.Type)
            .Should().BeEquivalentTo([typeof(InvoiceGenerationJob), typeof(CustomerNotificationJob)]);
    }

    [Fact]
    public async Task TheInvoiceCommand_CarriesTheEventAndTheCorrelationId()
    {
        var integrationEvent = AnEvent();

        await Dispatcher.PublishAsync(
            AnEnvelope(integrationEvent, correlationId: "corr-invoice"),
            TestContext.Current.CancellationToken);

        var command = client.SingleArgumentFor<InvoiceGenerationJob, GenerateInvoiceCommand>();

        command.JobId.Should().Be(integrationEvent.JobId);
        command.OrganizationId.Should().Be(integrationEvent.OrganizationId);
        command.CustomerId.Should().Be(integrationEvent.CustomerId);
        command.CompletedAtUtc.Should().Be(integrationEvent.CompletedAtUtc);
        command.IdempotencyKey.Should().Be(integrationEvent.IdempotencyKey);
        command.CorrelationId.Should().Be("corr-invoice");
    }

    [Fact]
    public async Task TheNotificationCommand_CarriesTheEventAndTheCorrelationId()
    {
        var integrationEvent = AnEvent();

        await Dispatcher.PublishAsync(
            AnEnvelope(integrationEvent, correlationId: "corr-mail"),
            TestContext.Current.CancellationToken);

        var command = client.SingleArgumentFor<CustomerNotificationJob, NotifyCustomerOfCompletionCommand>();

        command.JobId.Should().Be(integrationEvent.JobId);
        command.CustomerId.Should().Be(integrationEvent.CustomerId);
        command.CompletedAtUtc.Should().Be(integrationEvent.CompletedAtUtc);
        command.IdempotencyKey.Should().Be(integrationEvent.IdempotencyKey);
        command.CorrelationId.Should().Be("corr-mail");
    }

    [Fact]
    public async Task EachModule_IsEnqueuedOnItsOwnQueue()
    {
        await Dispatcher.PublishAsync(AnEnvelope(AnEvent()), TestContext.Current.CancellationToken);

        QueueFor<InvoiceGenerationJob>().Should().Be(InvoiceGenerationJob.Queue);
        QueueFor<CustomerNotificationJob>().Should().Be(CustomerNotificationJob.Queue);
    }

    [Fact]
    public async Task TheTypeNameFromBeforeTheContractMoved_IsStillDispatched()
    {
        var envelope = AnEnvelope(AnEvent()) with { Type = IntegrationEventTypes.LegacyJobCompleted };

        await Dispatcher.PublishAsync(envelope, TestContext.Current.CancellationToken);

        client.Enqueued.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnUnrecognisedType_ThrowsRatherThanSilentlyDroppingTheMessage()
    {
        var envelope = AnEnvelope(AnEvent()) with { Type = "Some.Future.IntegrationEvent" };

        var publish = async () => await Dispatcher.PublishAsync(envelope, TestContext.Current.CancellationToken);

        await publish.Should().ThrowAsync<UnknownIntegrationEventException>();
        client.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnreadablePayload_ThrowsRatherThanSilentlyDroppingTheMessage()
    {
        var envelope = AnEnvelope(AnEvent()) with { Content = "{ not json" };

        var publish = async () => await Dispatcher.PublishAsync(envelope, TestContext.Current.CancellationToken);

        await publish.Should().ThrowAsync<UnreadableIntegrationEventException>();
        client.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task ACancelledToken_EnqueuesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var publish = async () => await Dispatcher.PublishAsync(AnEnvelope(AnEvent()), cancellation.Token);

        await publish.Should().ThrowAsync<OperationCanceledException>();
        client.Enqueued.Should().BeEmpty();
    }

    private string QueueFor<T>() =>
        client.Enqueued
            .Single(entry => entry.Job.Type == typeof(T))
            .Job.Queue ?? EnqueuedState.DefaultQueue;

    private static JobCompletedIntegrationEvent AnEvent() =>
        new(
            EventId: Guid.CreateVersion7(),
            JobId: Guid.CreateVersion7(),
            OrganizationId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            CompletedAtUtc: CompletedAt,
            OccurredOnUtc: CompletedAt);

    private static OutboxEnvelope AnEnvelope(JobCompletedIntegrationEvent integrationEvent, string correlationId = "corr-1") =>
        new(
            integrationEvent.EventId,
            IntegrationEventTypes.JobCompleted,
            JsonSerializer.Serialize(integrationEvent, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            integrationEvent.OccurredOnUtc,
            correlationId);
}
