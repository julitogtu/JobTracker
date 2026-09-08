using JobTracker.Domain.Jobs;

namespace JobTracker.Tests.Domain;

/// <summary>
/// Builders for a <see cref="Job"/> in each status.
///
/// Every timestamp derives from <see cref="CreatedAt"/> so the ordering rules the aggregate
/// enforces (start after creation, completion after start, scheduling in the future) hold by
/// construction and a test only has to say which one it is deliberately breaking.
/// </summary>
internal static class JobFactory
{
    public static readonly DateTimeOffset CreatedAt = new(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset ScheduledFor = CreatedAt.AddDays(1);

    public static readonly DateTimeOffset StartedAt = ScheduledFor.AddMinutes(5);

    public static readonly DateTimeOffset CompletedAt = StartedAt.AddHours(2);

    public const string SignatureUrl = "https://example.com/signatures/abc.png";

    public const string PhotoUrl = "https://example.com/photos/abc.jpg";

    public static Address AnAddress() =>
        new("123 Main St", "Austin", "TX", "78701", 30.2672m, -97.7431m);

    public static Job Draft(DateTimeOffset? createdAt = null) => Job.Create(
        Guid.CreateVersion7(),
        "Fix HVAC",
        "Replace the failed compressor.",
        AnAddress(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        createdAt ?? CreatedAt);

    public static Job Scheduled()
    {
        var job = Draft();
        job.Schedule(ScheduledFor, Guid.CreateVersion7(), CreatedAt);

        return job;
    }

    public static Job InProgress()
    {
        var job = Scheduled();
        job.Start(StartedAt);

        return job;
    }

    public static Job Completed()
    {
        var job = InProgress();
        job.Complete(CompletedAt, SignatureUrl);

        return job;
    }

    public static Job Cancelled()
    {
        var job = Scheduled();
        job.Cancel(ScheduledFor, "Customer called it off.");

        return job;
    }
}
