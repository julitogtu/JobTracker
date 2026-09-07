using JobTracker.Domain.Enums;
using MediatR;

namespace JobTracker.Application.Jobs.Queries.SearchJobs;

public class SearchJobsQueryHandler : IRequestHandler<SearchJobsQuery, PagedList<JobDto>>
{
    public async Task<PagedList<JobDto>> Handle(SearchJobsQuery request, CancellationToken cancellationToken)
    {
        return await Task.FromResult(new PagedList<JobDto>(new List<JobDto>(), null, request.PageSize));
    }
}   