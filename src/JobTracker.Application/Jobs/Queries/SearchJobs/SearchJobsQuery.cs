using JobTracker.Application.Common.Behaviours;
using JobTracker.Application.Common.Results;
using JobTracker.Application.Jobs.Queries.Common;
using JobTracker.Domain.Enums;
using MediatR;

namespace JobTracker.Application.Jobs.Queries.SearchJobs;

public sealed record SearchJobsQuery(
    Guid OrganizationId,
    string? SearchTerm = null,
    IReadOnlyCollection<JobStatus>? Statuses = null,
    DateTimeOffset? ScheduledFromUtc = null,
    DateTimeOffset? ScheduledToUtc = null,
    Guid? AssigneeId = null,
    string? Cursor = null,
    int PageSize = 25)
    : IRequest<Result<PagedList<JobResponse>>>, IRetryableRequest;
