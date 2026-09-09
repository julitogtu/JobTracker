using System.Text.Json;
using Hangfire;
using JobTracker.Application.Common.Messaging;
using JobTracker.BackgroundJob.Billing;
using JobTracker.BackgroundJob.Correlation;
using JobTracker.BackgroundJob.Notifications;
using JobTracker.Jobs.IntegrationEvents;
using Serilog.Context;

namespace JobTracker.BackgroundJob.Outbox;

public sealed class HangfireOutboxDispatcher(IBackgroundJobClient backgroundJobs, ILogger<HangfireOutboxDispatcher> logger) : IOutboxMessagePublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var correlation = AmbientCorrelationIdAccessor.Push(message.CorrelationId);
        using var logProperty = LogContext.PushProperty("CorrelationId", message.CorrelationId);

        if (!IntegrationEventTypes.IsJobCompleted(message.Type))
        {
            throw new UnknownIntegrationEventException(message.Id, message.Type);
        }

        var integrationEvent = Deserialize(message);

        Dispatch(integrationEvent, message.CorrelationId);

        return Task.CompletedTask;
    }

    private void Dispatch(JobCompletedIntegrationEvent integrationEvent, string correlationId)
    {
        backgroundJobs.Enqueue<InvoiceGenerationJob>(InvoiceGenerationJob.Queue, job => job.ExecuteAsync(
            new GenerateInvoiceCommand(
                integrationEvent.JobId,
                integrationEvent.OrganizationId,
                integrationEvent.CustomerId,
                integrationEvent.CompletedAtUtc,
                integrationEvent.IdempotencyKey,
                correlationId),
            CancellationToken.None));

        backgroundJobs.Enqueue<CustomerNotificationJob>(CustomerNotificationJob.Queue, job => job.ExecuteAsync(
            new NotifyCustomerOfCompletionCommand(
                integrationEvent.JobId,
                integrationEvent.OrganizationId,
                integrationEvent.CustomerId,
                integrationEvent.CompletedAtUtc,
                integrationEvent.IdempotencyKey,
                correlationId),
            CancellationToken.None));

        logger.LogInformation(
            "Job {JobId} completed: queued invoice generation and customer notification.",
            integrationEvent.JobId);
    }

    private static JobCompletedIntegrationEvent Deserialize(OutboxEnvelope message)
    {
        JobCompletedIntegrationEvent? integrationEvent;

        try
        {
            integrationEvent = JsonSerializer.Deserialize<JobCompletedIntegrationEvent>(message.Content, SerializerOptions);
        }
        catch (JsonException exception)
        {
            throw new UnreadableIntegrationEventException(message.Id, message.Type, exception);
        }

        return integrationEvent ?? throw new UnreadableIntegrationEventException(message.Id, message.Type, innerException: null);
    }
}

public sealed class UnknownIntegrationEventException(Guid messageId, string type)
    : Exception($"Outbox message '{messageId}' has type '{type}', which this host does not handle.");

public sealed class UnreadableIntegrationEventException(Guid messageId, string type, Exception? innerException)
    : Exception($"Outbox message '{messageId}' of type '{type}' could not be deserialized.", innerException);
