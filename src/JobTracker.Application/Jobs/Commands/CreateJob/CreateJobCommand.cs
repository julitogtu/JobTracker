using JobTracker.Application.Common.Results;
using MediatR;

namespace JobTracker.Application.Jobs.Commands.CreateJob;

public sealed record CreateJobCommand(
    string Title,
    string Description,
    string Street,
    string City,
    string State,
    string ZipCode,
    decimal Latitude,
    decimal Longitude,
    Guid CustomerId,
    Guid OrganizationId,
    DateTimeOffset? ScheduledDateUtc = null,
    Guid? AssigneeId = null)
    : IRequest<Result<Guid>>;
