using JobTracker.Application.Common.Results;
using MediatR;

namespace JobTracker.Application.Jobs.Commands.StartJob;

public sealed record StartJobCommand(Guid OrganizationId, Guid JobId) : IRequest<Result<Unit>>;
