using JobTracker.Domain.Common;

namespace JobTracker.Domain.Jobs.Events;

public sealed record JobCreatedDomainEvent(
    Guid EventId,
    Guid JobId,
    Guid OrganizationId,
    Guid CustomerId,
    Guid? AssigneeId,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;
