namespace JobTracker.Domain.Jobs;

public sealed record JobSearchPage(IReadOnlyList<JobSearchItem> Items, string? NextCursor);
