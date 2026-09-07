using JobTracker.Persistence.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobTracker.Persistence.Database;

public sealed class JobsDbContextFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    public JobsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("JOBTRACKER_CONNECTION_STRING")
            ?? "Host=localhost;Port=5432;Database=jobtracker;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(JobsDbContext).Assembly.GetName().Name!))
            .AddInterceptors(new InsertOutboxMessagesInterceptor())
            .Options;

        return new JobsDbContext(options);
    }
}
