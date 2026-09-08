using JobTracker.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace JobTracker.Tests.Integration;

/// <summary>
/// One PostgreSQL container for the whole assembly, migrated once.
///
/// A real database is not optional here: the search path depends on a stored <c>tsvector</c>
/// computed column, a GIN index and PostgreSQL row-value comparison for the keyset cursor, none
/// of which the in-memory or SQLite providers can emulate. A test that passed against a fake
/// provider would say nothing about the query that actually ships.
///
/// Tests share the database rather than one container each, and isolate themselves by
/// organization id — see <see cref="IntegrationTestBase.OrganizationId"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("jobtracker")
        .WithUsername("jobtracker")
        .WithPassword("jobtracker_test_password")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);

        var services = new ServiceCollection()
            .AddPersistence(ConnectionString)
            .BuildServiceProvider();

        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<JobsDbContext>().Database.MigrateAsync(TestContext.Current.CancellationToken);
        await services.DisposeAsync();
    }

    public async ValueTask DisposeAsync() => await container.DisposeAsync();
}

/// <summary>
/// Binds every integration test class to the single <see cref="PostgresFixture"/>, so the
/// container starts once for the run instead of once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
