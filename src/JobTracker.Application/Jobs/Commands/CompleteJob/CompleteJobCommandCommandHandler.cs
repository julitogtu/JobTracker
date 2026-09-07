using JobTracker.Application.Common.Persistence;
using JobTracker.Application.Common.Results;
using JobTracker.Domain.Common;
using JobTracker.Domain.Enums;
using JobTracker.Domain.Jobs;
using MediatR;

namespace JobTracker.Application.Jobs.Commands.CompleteJob;

internal sealed class CompleteJobCommandHandler(IJobRepository jobRepository, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : IRequestHandler<CompleteJobCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(CompleteJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobRepository.GetByIdAsync(
            command.OrganizationId,
            command.JobId,
            cancellationToken);

        if (job is null)
        {
            return Result<Unit>.Failure(new Error("Jobs.Complete.NotFound", $"Job '{command.JobId}' was not found.", ErrorType.NotFound));
        }

        if (job.Status != JobStatus.InProgress)
        {
            return Result<Unit>.Failure(new Error("Jobs.Complete.InvalidStatus",$"Only an InProgress job can be completed. Current status: {job.Status}."));
        }

        try
        {
            job.Complete(timeProvider.GetUtcNow(), command.SignatureUrl);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<Unit>.Success(Unit.Value);
        }
        catch (DomainException exception)
        {
            return Result<Unit>.Failure(new Error("Jobs.Complete.DomainError", exception.Message));
        }
    }
}
