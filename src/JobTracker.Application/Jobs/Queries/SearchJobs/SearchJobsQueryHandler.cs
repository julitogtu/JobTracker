using JobTracker.Application.Common.Results;
using JobTracker.Application.Jobs.Queries.Common;
using JobTracker.Domain.Jobs;
using MediatR;

namespace JobTracker.Application.Jobs.Queries.SearchJobs;

internal sealed class SearchJobsQueryHandler(IJobRepository jobRepository) : IRequestHandler<SearchJobsQuery, Result<PagedList<JobResponse>>>
{
    public async Task<Result<PagedList<JobResponse>>> Handle(SearchJobsQuery query, CancellationToken cancellationToken)
    {
        try
        {
            var criteria = new JobSearchCriteria(
                OrganizationId: query.OrganizationId,
                SearchTerm: query.SearchTerm,
                Statuses: query.Statuses,
                ScheduledFromUtc: query.ScheduledFromUtc,
                ScheduledToUtc: query.ScheduledToUtc,
                AssigneeId: query.AssigneeId,
                Cursor: query.Cursor,
                PageSize: query.PageSize);

            var page = await jobRepository.SearchAsync(criteria, cancellationToken);

            var responses = page.Items
                .Select(item => JobResponse.FromSearchItem(item))
                .ToList();

            var result = new PagedList<JobResponse>(
                Items: responses,
                NextCursor: page.NextCursor,
                PageSize: query.PageSize);

            return Result<PagedList<JobResponse>>.Success(result);
        }
        catch (Exception exception)
        {
            return Result<PagedList<JobResponse>>.Failure(Error.Validation("Jobs.Search.InvalidCursor", exception.Message));
        }
    }
}