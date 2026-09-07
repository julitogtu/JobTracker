using MediatR;

namespace JobTracker.Application.Jobs.Commands.CreateJob;

public class CreateJobCommandHandler : IRequestHandler<CreateJobCommand, Guid>
{
    public async Task<Guid> Handle(CreateJobCommand request, CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid(); // Replace with actual job creation logic
        return await Task.FromResult(jobId);
    }
}
