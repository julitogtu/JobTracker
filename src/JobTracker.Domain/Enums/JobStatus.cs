namespace JobTracker.Domain.Enums;

public enum JobStatus
{
    Draft = 1,
    Scheduled = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5
}