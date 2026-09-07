namespace JobTracker.Domain.Jobs;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken = default);

    Task AddAsync(Job job, CancellationToken cancellationToken = default);

    Task<JobSearchPage> SearchAsync(JobSearchCriteria criteria, CancellationToken cancellationToken = default);
}
