namespace JobTracker.Jobs.IntegrationEvents;

public static class IntegrationEventTypes
{
    public static readonly string JobCompleted = typeof(JobCompletedIntegrationEvent).FullName!;

    public const string LegacyJobCompleted =
        "JobTracker.Domain.Jobs.IntegrationEvents.JobCompletedIntegrationEvent";

    public static bool IsJobCompleted(string type) =>
        string.Equals(type, JobCompleted, StringComparison.Ordinal)
        || string.Equals(type, LegacyJobCompleted, StringComparison.Ordinal);
}
