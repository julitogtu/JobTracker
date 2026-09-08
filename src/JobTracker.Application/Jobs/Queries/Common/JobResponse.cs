using JobTracker.Domain.Enums;
using JobTracker.Domain.Jobs;

namespace JobTracker.Application.Jobs.Queries.Common;

public sealed record JobResponse(
    Guid Id,
    string Title,
    string Description,
    JobStatus Status,
    DateTimeOffset? ScheduledDateUtc,
    Guid? AssigneeId,
    Guid CustomerId,
    Guid OrganizationId,
    AddressResponse Address,
    int PhotoCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static JobResponse FromDomain(Job job)
    {
        return new JobResponse(
            Id: job.Id,
            Title: job.Title,
            Description: job.Description,
            Status: job.Status,
            ScheduledDateUtc: job.ScheduledDateUtc,
            AssigneeId: job.AssigneeId,
            CustomerId: job.CustomerId,
            OrganizationId: job.OrganizationId,
            Address: new AddressResponse(
                job.Address.Street,
                job.Address.City,
                job.Address.State,
                job.Address.ZipCode,
                job.Address.Latitude,
                job.Address.Longitude),
            PhotoCount: job.Photos.Count,
            CreatedAtUtc: job.CreatedAtUtc,
            UpdatedAtUtc: job.UpdatedAtUtc);
    }

    public static JobResponse FromSearchItem(JobSearchItem job)
    {
        return new JobResponse(
            Id: job.Id,
            Title: job.Title,
            Description: job.Description,
            Status: job.Status,
            ScheduledDateUtc: job.ScheduledDateUtc,
            AssigneeId: job.AssigneeId,
            CustomerId: job.CustomerId,
            OrganizationId: job.OrganizationId,
            Address: new AddressResponse(
                job.Street,
                job.City,
                job.State,
                job.ZipCode,
                job.Latitude,
                job.Longitude),
            PhotoCount: job.PhotoCount,
            CreatedAtUtc: job.CreatedAtUtc,
            UpdatedAtUtc: job.UpdatedAtUtc);
    }
}

public sealed record AddressResponse(
    string Street,
    string City,
    string State,
    string ZipCode,
    decimal Latitude,
    decimal Longitude);
