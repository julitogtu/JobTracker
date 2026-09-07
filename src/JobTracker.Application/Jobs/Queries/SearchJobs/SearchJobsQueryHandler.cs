using MediatR;

namespace JobTracker.Application.Jobs.Queries.SearchJobs;

public class SearchJobsQueryHandler : IRequestHandler<SearchJobsQuery, IReadOnlyList<JobDto>>
{
    public async Task<IReadOnlyList<JobDto>> Handle(SearchJobsQuery request, CancellationToken cancellationToken)
    {
        var jobs = new List<JobDto>();
        return await Task.FromResult(jobs);
    }
}   