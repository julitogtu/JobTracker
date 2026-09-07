using JobTracker.Application.Common.Messaging;
using JobTracker.Application.Common.Persistence;
using JobTracker.Domain.Jobs;
using JobTracker.Persistence.Database;
using JobTracker.Persistence.Outbox;
using JobTracker.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<InsertOutboxMessagesInterceptor>();

        services.AddDbContext<JobsDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(JobsDbContext).Assembly.GetName().Name!));
            options.AddInterceptors(
                serviceProvider.GetRequiredService<InsertOutboxMessagesInterceptor>());
        });

        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxMessagePublisher, OutboxMessagePublisher>();
        services.AddScoped<OutboxMessageProcessor>();

        return services;
    }
}
