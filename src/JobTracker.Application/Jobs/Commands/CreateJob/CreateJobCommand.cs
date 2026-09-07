using MediatR;

namespace JobTracker.Application.Jobs.Commands.CreateJob;

public class CreateJobCommand : IRequest<Guid>
{
}
