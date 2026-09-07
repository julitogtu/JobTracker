using MediatR;

namespace JobTracker.Application.Jobs.Queries.SearchJobs;

public class SearchJobsQuery : IRequest<IReadOnlyList<JobDto>>
{
}
