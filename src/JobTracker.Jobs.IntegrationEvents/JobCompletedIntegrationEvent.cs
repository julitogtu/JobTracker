namespace JobTracker.Jobs.IntegrationEvents;

public sealed record JobCompletedIntegrationEvent(Guid EventId, Guid JobId, Guid OrganizationId, Guid CustomerId, DateTimeOffset CompletedAtUtc, DateTimeOffset OccurredOnUtc)
{
    public string IdempotencyKey => $"{JobId:N}:{CompletedAtUtc:O}";
}
