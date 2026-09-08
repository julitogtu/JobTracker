using JobTracker.Application.Common.Correlation;
using JobTracker.Domain.Common;
using JobTracker.Domain.Jobs.Events;
using JobTracker.Domain.Jobs.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace JobTracker.Persistence.Outbox;

public sealed class InsertOutboxMessagesInterceptor(ICorrelationIdAccessor correlationIdAccessor)
    : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        InsertOutboxMessages(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        InsertOutboxMessages(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void InsertOutboxMessages(
        DbContext? dbContext)
    {
        if (dbContext is null)
            return;

        var completedEvents = dbContext.ChangeTracker
            .Entries<AggregateRoot>()
            .SelectMany(entry =>
                entry.Entity.DomainEvents
                    .OfType<JobCompletedDomainEvent>()
                    .Select(domainEvent => new
                    {
                        Aggregate = entry.Entity,
                        DomainEvent = domainEvent
                    }))
            .ToArray();

        if (completedEvents.Length == 0)
            return;

        var trackedMessageIds = dbContext.ChangeTracker
            .Entries<OutboxMessage>()
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        foreach (var item in completedEvents)
        {
            var domainEvent = item.DomainEvent;

            if (!trackedMessageIds.Contains(domainEvent.EventId))
            {
                var integrationEvent = CreateIntegrationEvent(domainEvent);

                var outboxMessage = OutboxMessage.Create(
                    id: integrationEvent.EventId,
                    type: typeof(JobCompletedIntegrationEvent).FullName!,
                    content: JsonSerializer.Serialize(integrationEvent,SerializerOptions),
                    occurredOnUtc: integrationEvent.OccurredOnUtc,
                    correlationId: correlationIdAccessor.CorrelationId);

                dbContext.Set<OutboxMessage>().Add(outboxMessage);

                trackedMessageIds.Add(integrationEvent.EventId);
            }

            item.Aggregate.RemoveDomainEvent(domainEvent);
        }
    }

    private static JobCompletedIntegrationEvent CreateIntegrationEvent(JobCompletedDomainEvent domainEvent)
        => new JobCompletedIntegrationEvent(
            EventId: domainEvent.EventId,
            JobId: domainEvent.JobId,
            OrganizationId: domainEvent.OrganizationId,
            CustomerId: domainEvent.CustomerId,
            CompletedAtUtc: domainEvent.CompletedAtUtc,
            OccurredOnUtc: domainEvent.OccurredOnUtc);
}
