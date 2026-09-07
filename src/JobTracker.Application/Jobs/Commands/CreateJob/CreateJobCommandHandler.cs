using JobTracker.Application.Common.Persistence;
using JobTracker.Application.Common.Results;
using JobTracker.Domain.Common;
using JobTracker.Domain.Jobs;
using MediatR;

namespace JobTracker.Application.Jobs.Commands.CreateJob;

internal sealed class CreateJobCommandHandler(
    IJobRepository jobRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateJobCommand, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateJobCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var address = new Address(
                street: command.Street,
                city: command.City,
                state: command.State,
                zipCode: command.ZipCode,
                latitude: command.Latitude,
                longitude: command.Longitude);

            var job = Job.Create(
                id: Guid.CreateVersion7(),
                title: command.Title,
                description: command.Description,
                address: address,
                customerId: command.CustomerId,
                organizationId: command.OrganizationId,
                occurredOnUtc: timeProvider.GetUtcNow(),
                scheduledDate: command.ScheduledDateUtc,
                assigneeId: command.AssigneeId);

            await jobRepository.AddAsync(
                job,
                cancellationToken);

            await unitOfWork.SaveChangesAsync(
                cancellationToken);

            return Result<Guid>.Success(job.Id);
        }
        catch (DomainException exception)
        {
            return Result<Guid>.Failure(Error.Validation("Jobs.Create.Invalid", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Result<Guid>.Failure(Error.Validation("Jobs.Create.InvalidInput", exception.Message));
        }
    }
}
