using JobTracker.Domain.Common;

namespace JobTracker.Domain.Jobs.Events;

public sealed record JobCancelledDomainEvent(
    Guid EventId,
    Guid JobId,
    Guid OrganizationId,
    Guid CustomerId,
    DateTimeOffset CancelledAtUtc,
    string Reason,
    DateTimeOffset OccurredOnUtc) : IDomainEvent;