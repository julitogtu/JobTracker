namespace JobTracker.Domain.Jobs;

public sealed record JobSearchPage(IReadOnlyList<Job> Items, string? NextCursor);
