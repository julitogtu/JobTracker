using MediatR;

namespace JobTracker.Application.Jobs.Commands.CompleteJob;

public class CompleteJobCommandCommandHandler : IRequestHandler<CompleteJobCommand, Unit>
{
    public async Task<Unit> Handle(CompleteJobCommand request, CancellationToken cancellationToken)
    {
        var jobId = Guid.NewGuid();
        return await Task.FromResult(Unit.Value);
    }
}
