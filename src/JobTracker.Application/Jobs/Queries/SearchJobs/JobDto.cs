using JobTracker.Domain.Enums;

namespace JobTracker.Application.Jobs.Queries.SearchJobs;

public sealed record JobDto(
    Guid Id,
    string Title,
    string Description,
    JobStatus Status,
    DateTimeOffset? ScheduledDateUtc,
    Guid? AssigneeId,
    Guid CustomerId,
    Guid OrganizationId,
    string Street,
    string City,
    string State,
    string ZipCode,
    decimal Latitude,
    decimal Longitude,
    int PhotoCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);