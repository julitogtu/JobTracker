using JobTracker.Domain.Jobs;
using JobTracker.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;

namespace JobTracker.Persistence.Database;

public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    public DbSet<Job> Jobs => Set<Job>();

    public DbSet<JobPhoto> JobPhotos => Set<JobPhoto>();

    internal DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("jobs");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobsDbContext).Assembly);
    }
}
