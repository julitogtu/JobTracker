using JobTracker.Domain.Enums;

namespace JobTracker.Domain.Jobs;

public sealed record JobSearchCriteria(
    Guid OrganizationId,
    string? SearchTerm = null,
    IReadOnlyCollection<JobStatus>? Statuses = null,
    DateTimeOffset? ScheduledFromUtc = null,
    DateTimeOffset? ScheduledToUtc = null,
    Guid? AssigneeId = null,
    string? Cursor = null,
    int PageSize = 25);