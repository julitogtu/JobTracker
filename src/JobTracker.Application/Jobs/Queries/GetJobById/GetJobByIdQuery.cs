using JobTracker.Application.Common.Results;
using JobTracker.Application.Jobs.Queries.Common;
using JobTracker.Domain.Jobs;
using MediatR;

namespace JobTracker.Application.Jobs.Queries.GetJobById;

public sealed record GetJobByIdQuery(Guid OrganizationId, Guid JobId) : IRequest<Result<JobResponse>>;

internal sealed class GetJobByIdQueryHandler(IJobRepository jobRepository) : IRequestHandler<GetJobByIdQuery, Result<JobResponse>>
{
    public async Task<Result<JobResponse>> Handle(GetJobByIdQuery query, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(query.OrganizationId, query.JobId, cancellationToken);

        if (job is null)
        {
            return Result<JobResponse>.Failure(Error.NotFound("Jobs.GetById.NotFound", $"Job '{query.JobId}' was not found."));
        }

        return Result<JobResponse>.Success(JobResponse.FromDomain(job));
    }
}