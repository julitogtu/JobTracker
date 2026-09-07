using JobTracker.Application.Common.Results;
using MediatR;

namespace JobTracker.Application.Jobs.Commands.CompleteJob;

public sealed record CompleteJobCommand(
    Guid OrganizationId,
    Guid JobId,
    string SignatureUrl)
    : IRequest<Result<Unit>>;
