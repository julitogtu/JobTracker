using JobTracker.Domain.Common;

namespace JobTracker.Domain.Jobs.Events;

public sealed record JobCompletedDomainEvent(
    Guid EventId,
    Guid JobId,
    Guid OrganizationId,
    Guid CustomerId,
    DateTimeOffset CompletedAtUtc,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;