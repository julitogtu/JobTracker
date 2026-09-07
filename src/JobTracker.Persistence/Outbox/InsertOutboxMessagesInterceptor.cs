using JobTracker.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

namespace JobTracker.Persistence.Outbox;

public sealed class InsertOutboxMessagesInterceptor : SaveChangesInterceptor
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

    private static void InsertOutboxMessages(DbContext? dbContext)
    {
        if (dbContext is null)
            return;

        var aggregates = dbContext.ChangeTracker
            .Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToArray();

        if (aggregates.Length == 0)
            return;

        var alreadyTrackedIds = dbContext.ChangeTracker
            .Entries<OutboxMessage>()
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

        var messages = aggregates
            .SelectMany(aggregate => aggregate.DomainEvents)
            .Where(domainEvent => !alreadyTrackedIds.Contains(domainEvent.EventId))
            .Select(domainEvent => OutboxMessage.Create(
                domainEvent.EventId,
                domainEvent.GetType().FullName ?? throw new InvalidOperationException("A domain event must have a stable type name."),
                JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), SerializerOptions),
                domainEvent.OccurredOnUtc))
            .ToArray();

        dbContext.Set<OutboxMessage>().AddRange(messages);

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }
    }
}
