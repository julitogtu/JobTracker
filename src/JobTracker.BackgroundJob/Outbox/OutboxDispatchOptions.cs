namespace JobTracker.BackgroundJob.Outbox;

public sealed class OutboxDispatchOptions
{
    public const string SectionName = "OutboxDispatch";

    public string CronExpression { get; set; } = "* * * * *";

    public int BatchSize { get; set; } = 50;

    public int MaxBatchesPerRun { get; set; } = 5;
}
