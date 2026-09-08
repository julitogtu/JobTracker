using FluentAssertions;
using JobTracker.Application.Common.Correlation;
using JobTracker.Application.Common.Messaging;
using JobTracker.Persistence.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobTracker.Tests.Integration;

public sealed class OutboxCorrelationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<Guid> CompleteAJobAsync()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(job.Id);

        tracked!.Complete(Clock.GetUtcNow(), "https://example.com/signatures/abc.png");
        await SaveChangesAsync();

        return job.Id;
    }

    [Fact]
    public async Task OutboxMessage_CarriesTheRequestCorrelationId()
    {
        Correlation.CorrelationId = "req-0199a4c1-1f5e";

        var jobId = await CompleteAJobAsync();

        var row = (await ReadOutboxAsync(jobId)).Single();

        row.CorrelationId.Should().Be("req-0199a4c1-1f5e");
    }

    [Fact]
    public async Task MessagesFromDifferentRequests_CarryTheirOwnCorrelationId()
    {
        Correlation.CorrelationId = "req-first";
        var first = await CompleteAJobAsync();

        Correlation.CorrelationId = "req-second";
        var second = await CompleteAJobAsync();

        (await ReadOutboxAsync(first)).Single().CorrelationId.Should().Be("req-first");
        (await ReadOutboxAsync(second)).Single().CorrelationId.Should().Be("req-second");
    }

    [Fact]
    public async Task CorrelationId_IsQueryableAlongsideThePayload()
    {
        Correlation.CorrelationId = "req-queryable";

        var jobId = await CompleteAJobAsync();

        var matches = await CountAsync(
            "outbox_messages",
            "correlation_id = @correlationId AND content ->> 'jobId' = @jobId",
            ("correlationId", "req-queryable"),
            ("jobId", jobId.ToString()));

        matches.Should().Be(1);
    }

    [Fact]
    public async Task DrainingTheOutbox_HandsTheCorrelationIdToThePublisher()
    {
        Correlation.CorrelationId = "req-drained";

        var jobId = await CompleteAJobAsync();

        var publisher = new CapturingPublisher();
        await using var provider = BuildProviderWith(publisher);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<OutboxMessageProcessor>()
            .ExecuteAsync(cancellationToken: Ct);

        var envelope = publisher.Published
            .Should().ContainSingle(message => message.Content.Contains(jobId.ToString()))
            .Subject;

        envelope.CorrelationId.Should().Be("req-drained");
    }

    [Fact]
    public async Task ProcessedMessage_KeepsItsCorrelationId()
    {
        Correlation.CorrelationId = "req-retained";

        var jobId = await CompleteAJobAsync();

        var publisher = new CapturingPublisher();
        await using var provider = BuildProviderWith(publisher);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<OutboxMessageProcessor>()
            .ExecuteAsync(cancellationToken: Ct);

        var row = (await ReadOutboxAsync(jobId)).Single();

        row.ProcessedOnUtc.Should().NotBeNull();
        row.CorrelationId.Should().Be("req-retained");
    }

    [Fact]
    public void PersistenceAlone_FallsBackToTheNullAccessor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence("Host=localhost;Database=unused;Username=unused;Password=unused");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var accessor = scope.ServiceProvider.GetRequiredService<ICorrelationIdAccessor>();

        accessor.Should().BeOfType<NullCorrelationIdAccessor>();
        accessor.CorrelationId.Should().Be(ICorrelationIdAccessor.None);
    }

    [Fact]
    public async Task WithoutARequestInScope_TheMessageIsStampedWithNone()
    {
        Correlation.CorrelationId = ICorrelationIdAccessor.None;

        var jobId = await CompleteAJobAsync();

        (await ReadOutboxAsync(jobId)).Single().CorrelationId.Should().Be(ICorrelationIdAccessor.None);
    }

    private ServiceProvider BuildProviderWith(IOutboxMessagePublisher publisher)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICorrelationIdAccessor>(Correlation);
        services.AddApplication();
        services.AddPersistence(ConnectionString);
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IOutboxMessagePublisher>(publisher);

        return services.BuildServiceProvider();
    }

    private sealed class CapturingPublisher : IOutboxMessagePublisher
    {
        private readonly List<OutboxEnvelope> published = [];

        public IReadOnlyList<OutboxEnvelope> Published
        {
            get
            {
                lock (published)
                {
                    return published.ToList();
                }
            }
        }

        public Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            lock (published)
            {
                published.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
