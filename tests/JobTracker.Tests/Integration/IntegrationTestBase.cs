using System.Data.Common;
using JobTracker.Application.Common.Correlation;
using JobTracker.Application.Common.Persistence;
using JobTracker.Domain.Jobs;
using JobTracker.Persistence.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobTracker.Tests.Integration;

/// <summary>
/// Base for tests that run against the real database through the real composition root:
/// <c>AddApplication()</c> + <c>AddPersistence()</c>, the same two calls <c>Program.cs</c> makes.
/// Nothing is substituted except the clock, so handlers, the repository, EF mappings, the outbox
/// interceptor and the SQL that Npgsql actually emits are all under test.
///
/// Isolation comes from <see cref="OrganizationId"/>: xunit constructs a new instance per test,
/// each gets a fresh organization id, and every repository query is tenant-scoped — so tests
/// share one database without seeing each other's rows and need no truncation between them.
/// Outbox rows are the exception (the table carries no organization column), so outbox
/// assertions match on the job id inside the payload.
///
/// The <c>Send</c> / <c>GetJobAsync</c> / <c>SearchAsync</c> wrappers exist to thread
/// <see cref="Ct"/> through every call in one place, so a cancelled run actually stops instead
/// of waiting on the database.
/// </summary>
[Collection(PostgresCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    /// <summary>Fixed "now" for every test, so scheduling and ordering are deterministic.</summary>
    protected static readonly DateTimeOffset Now = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider provider;
    private AsyncServiceScope scope;

    protected IntegrationTestBase(PostgresFixture fixture)
    {
        Clock = new MutableTimeProvider(Now);
        ConnectionString = fixture.ConnectionString;

        var services = new ServiceCollection();

        // The web host registers logging for free; a bare ServiceCollection does not, and
        // AddMediatR fails at resolve time without an ILoggerFactory.
        services.AddLogging();

        services.AddSingleton<ICorrelationIdAccessor>(Correlation);

        services.AddApplication();
        services.AddPersistence(fixture.ConnectionString);

        // AddPersistence registers TimeProvider.System; the last registration wins, so this
        // replaces it without the production wiring knowing about tests.
        services.AddSingleton<TimeProvider>(Clock);

        provider = services.BuildServiceProvider();
    }

    /// <summary>Unique per test — the tenant boundary that keeps tests from colliding.</summary>
    protected Guid OrganizationId { get; } = Guid.CreateVersion7();

    protected MutableTimeProvider Clock { get; }

    protected string ConnectionString { get; }

    protected MutableCorrelationIdAccessor Correlation { get; } =
        new($"test-{Guid.CreateVersion7()}");

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected IMediator Mediator => scope.ServiceProvider.GetRequiredService<IMediator>();

    protected IJobRepository Jobs => scope.ServiceProvider.GetRequiredService<IJobRepository>();

    protected IUnitOfWork UnitOfWork => scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

    protected JobsDbContext Db => scope.ServiceProvider.GetRequiredService<JobsDbContext>();

    public ValueTask InitializeAsync()
    {
        scope = provider.CreateAsyncScope();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
    }

    /* -------------------------------------------------------------------------------------
     * The production entry points, with the test's cancellation token attached.
     * ---------------------------------------------------------------------------------- */

    protected Task<TResponse> Send<TResponse>(IRequest<TResponse> request) =>
        Mediator.Send(request, Ct);

    protected Task<Job?> GetJobAsync(Guid jobId) =>
        Jobs.GetByIdAsync(OrganizationId, jobId, Ct);

    protected Task<Job?> GetJobAsync(Guid organizationId, Guid jobId) =>
        Jobs.GetByIdAsync(organizationId, jobId, Ct);

    protected Task<JobSearchPage> SearchAsync(JobSearchCriteria criteria) =>
        Jobs.SearchAsync(criteria, Ct);

    protected Task AddJobAsync(Job job) => Jobs.AddAsync(job, Ct);

    protected Task<int> SaveChangesAsync() => UnitOfWork.SaveChangesAsync(Ct);

    /* -------------------------------------------------------------------------------------
     * Seeding helpers. These go through the aggregate and the repository rather than raw SQL,
     * so a seeded row is a row the production write path could actually have produced.
     * ---------------------------------------------------------------------------------- */

    protected static Address AnAddress() =>
        new("123 Main St", "Austin", "TX", "78701", 30.267200m, -97.743100m);

    protected Job NewDraft(
        string title = "Fix HVAC",
        string description = "Replace the failed compressor.",
        Guid? organizationId = null,
        Guid? customerId = null) =>
        Job.Create(
            Guid.CreateVersion7(),
            title,
            description,
            AnAddress(),
            customerId ?? Guid.CreateVersion7(),
            organizationId ?? OrganizationId,
            Clock.GetUtcNow());

    /// <summary>Persists a job and detaches it, so the next read comes from the database.</summary>
    protected async Task<Job> SaveAsync(Job job)
    {
        await AddJobAsync(job);
        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        return job;
    }

    protected Task<Job> SaveDraftAsync(
        string title = "Fix HVAC",
        string description = "Replace the failed compressor.",
        Guid? organizationId = null) =>
        SaveAsync(NewDraft(title, description, organizationId));

    protected Task<Job> SaveScheduledAsync(
        DateTimeOffset? scheduledFor = null,
        Guid? assigneeId = null,
        Guid? organizationId = null,
        string title = "Fix HVAC",
        string description = "Replace the failed compressor.")
    {
        var job = NewDraft(title, description, organizationId);
        job.Schedule(scheduledFor ?? Now.AddDays(1), assigneeId ?? Guid.CreateVersion7(), Clock.GetUtcNow());

        return SaveAsync(job);
    }

    protected Task<Job> SaveInProgressAsync(
        DateTimeOffset? scheduledFor = null,
        Guid? assigneeId = null,
        Guid? organizationId = null)
    {
        var job = NewDraft(organizationId: organizationId);
        job.Schedule(scheduledFor ?? Now.AddDays(1), assigneeId ?? Guid.CreateVersion7(), Clock.GetUtcNow());
        job.Start(Clock.GetUtcNow());

        return SaveAsync(job);
    }

    /* -------------------------------------------------------------------------------------
     * Raw SQL. The outbox entity and DbSet are internal to the Persistence assembly, and
     * asserting against real columns is the point of an integration test anyway: it catches a
     * mapping change that a round-trip through EF would hide.
     * ---------------------------------------------------------------------------------- */

    protected async Task<IReadOnlyList<OutboxRow>> ReadOutboxAsync(Guid jobId)
    {
        var rows = new List<OutboxRow>();

        await using var command = await CreateCommandAsync("""
            SELECT id, type, content::text, occurred_on_utc, processed_on_utc, retry_count, next_attempt_on_utc, correlation_id
            FROM jobs.outbox_messages
            WHERE content ->> 'jobId' = @jobId
            ORDER BY occurred_on_utc
            """);

        AddParameter(command, "jobId", jobId.ToString());

        await using var reader = await command.ExecuteReaderAsync(Ct);

        while (await reader.ReadAsync(Ct))
        {
            rows.Add(new OutboxRow(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                await reader.IsDBNullAsync(4, Ct) ? null : reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetInt32(5),
                reader.GetFieldValue<DateTimeOffset>(6),
                reader.GetString(7)));
        }

        return rows;
    }

    /// <summary>Reads one column straight out of the jobs table, bypassing every EF mapping.</summary>
    protected async Task<T?> ReadJobColumnAsync<T>(Guid jobId, string column)
    {
        await using var command = await CreateCommandAsync(
            $"SELECT {column} FROM jobs.jobs WHERE id = @id");

        AddParameter(command, "id", jobId);

        var value = await command.ExecuteScalarAsync(Ct);

        return value is null or DBNull ? default : (T)value;
    }

    protected async Task<long> CountAsync(
        string table,
        string where,
        params (string Name, object Value)[] parameters)
    {
        await using var command = await CreateCommandAsync($"SELECT count(*) FROM jobs.{table} WHERE {where}");

        foreach (var (name, value) in parameters)
        {
            AddParameter(command, name, value);
        }

        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<DbCommand> CreateCommandAsync(string sql)
    {
        var connection = Db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(Ct);
        }

        var command = connection.CreateCommand();
        command.CommandText = sql;

        // Npgsql rejects a command that ignores the connection's active transaction, so the
        // raw reads have to enlist in whatever EF has open.
        command.Transaction = Db.Database.CurrentTransaction?.GetDbTransaction();

        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    protected sealed record OutboxRow(
        Guid Id,
        string Type,
        string Content,
        DateTimeOffset OccurredOnUtc,
        DateTimeOffset? ProcessedOnUtc,
        int RetryCount,
        DateTimeOffset NextAttemptOnUtc,
        string CorrelationId);
}

public sealed class MutableCorrelationIdAccessor(string correlationId) : ICorrelationIdAccessor
{
    public string CorrelationId { get; set; } = correlationId;
}

/// <summary>
/// A <see cref="TimeProvider"/> whose "now" the test sets. Handlers take the clock by injection
/// rather than calling <c>DateTime.UtcNow</c>, so this is all it takes to make timestamps and
/// scheduling deterministic.
/// </summary>
public sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
