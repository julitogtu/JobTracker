using JobTracker.Application.Common.Persistence;

namespace JobTracker.Persistence.Database;

internal sealed class UnitOfWork(JobsDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
