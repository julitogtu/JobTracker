using JobTracker.Application.Common.Persistence;
using JobTracker.Application.Common.Results;
using JobTracker.Domain.Common;
using JobTracker.Domain.Enums;
using JobTracker.Domain.Jobs;
using MediatR;

namespace JobTracker.Application.Jobs.Commands.StartJob;

internal sealed class StartJobCommandHandler(IJobRepository jobRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : IRequestHandler<StartJobCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(StartJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(command.OrganizationId, command.JobId, cancellationToken);

        if (job is null)
        {
            return Result<Unit>.Failure(Error.NotFound("Jobs.Start.NotFound",$"Job '{command.JobId}' was not found."));
        }

        if (job.Status != JobStatus.Scheduled)
        {
            return Result<Unit>.Failure(Error.Conflict("Jobs.Start.InvalidStatus", $"Only a Scheduled job can be started. Current status: {job.Status}."));
        }

        try
        {
            job.Start(timeProvider.GetUtcNow());

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (DomainException exception)
        {
            return Result<Unit>.Failure(Error.Conflict("Jobs.Start.DomainError",exception.Message));
        }
    }
}