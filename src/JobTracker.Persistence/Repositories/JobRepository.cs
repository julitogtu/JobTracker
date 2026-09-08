using JobTracker.Domain.Jobs;
using JobTracker.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using System.Text.Json;

namespace JobTracker.Persistence.Repositories;

internal sealed partial class JobRepository(JobsDbContext dbContext) : IJobRepository
{
    private static readonly DateTimeOffset UnscheduledSortValue = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    public Task<Job?> GetByIdAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken = default) =>
        dbContext.Jobs
            .Include(job => job.Photos)
            .SingleOrDefaultAsync(job => job.OrganizationId == organizationId && job.Id == jobId, cancellationToken);

    public async Task AddAsync(Job job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        await dbContext.Jobs.AddAsync(job, cancellationToken);
    }

    public async Task<JobSearchPage> SearchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var pageSize = Math.Clamp(
            criteria.PageSize,
            1,
            100);

        var query = dbContext.Jobs
            .AsNoTracking()
            .Where(job =>
                job.OrganizationId ==
                criteria.OrganizationId);

        if (criteria.Statuses is { Count: > 0 })
        {
            query = query.Where(job =>
                criteria.Statuses.Contains(job.Status));
        }

        if (criteria.AssigneeId.HasValue)
        {
            query = query.Where(job =>
                job.AssigneeId ==
                criteria.AssigneeId.Value);
        }

        if (criteria.ScheduledFromUtc.HasValue)
        {
            var fromUtc = criteria
                .ScheduledFromUtc
                .Value
                .ToUniversalTime();

            query = query.Where(job =>
                job.ScheduledDateUtc >= fromUtc);
        }

        if (criteria.ScheduledToUtc.HasValue)
        {
            var toUtc = criteria
                .ScheduledToUtc
                .Value
                .ToUniversalTime();

            query = query.Where(job =>
                job.ScheduledDateUtc <= toUtc);
        }

        if (!string.IsNullOrWhiteSpace(
                criteria.SearchTerm))
        {
            var searchTerm =
                criteria.SearchTerm.Trim();

            query = query.Where(job =>
                EF.Property<NpgsqlTsVector>(job, "SearchVector")
                    .Matches(EF.Functions.WebSearchToTsQuery("english", searchTerm)));
        }

        var cursor = DecodeCursor(criteria.Cursor);

        if (cursor is not null)
        {
            query = query.Where(job =>
                EF.Functions.GreaterThan(
                    ValueTuple.Create(
                        job.ScheduledDateUtc ??
                        UnscheduledSortValue,
                        job.Id),
                    ValueTuple.Create(
                        cursor.SortDateUtc,
                        cursor.JobId)));
        }

        var jobs = await query
            .OrderBy(job => job.ScheduledDateUtc ?? UnscheduledSortValue)
            .ThenBy(job => job.Id)
            .Select(job => new JobSearchItem(
                job.Id,
                job.Title,
                job.Description,
                job.Status,
                job.ScheduledDateUtc,
                job.AssigneeId,
                job.CustomerId,
                job.OrganizationId,
                job.Address.Street,
                job.Address.City,
                job.Address.State,
                job.Address.ZipCode,
                job.Address.Latitude,
                job.Address.Longitude,
                job.Photos.Count,
                job.CreatedAtUtc,
                job.UpdatedAtUtc))
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasNextPage =
            jobs.Count > pageSize;

        if (hasNextPage)
        {
            jobs.RemoveAt(pageSize);
        }

        var nextCursor =hasNextPage && jobs.Count > 0 ? EncodeCursor(jobs[^1]) : null;

        return new JobSearchPage(Items: jobs, NextCursor: nextCursor);
    }

    private static string EncodeCursor(JobSearchItem job)
    {
        var cursor = new JobCursor(job.ScheduledDateUtc ?? UnscheduledSortValue, job.Id);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cursor);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static JobCursor? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
            return JsonSerializer.Deserialize<JobCursor>(Convert.FromBase64String(normalized));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("The pagination cursor is invalid.", nameof(value), exception);
        }
    }

    private sealed record JobCursor(DateTimeOffset SortDateUtc, Guid JobId);
}
