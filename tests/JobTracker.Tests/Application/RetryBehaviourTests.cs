using System.Data.Common;
using FluentAssertions;
using JobTracker.Application.Common.Behaviours;
using JobTracker.Application.Jobs.Commands.CompleteJob;
using JobTracker.Application.Jobs.Commands.CreateJob;
using JobTracker.Application.Jobs.Commands.StartJob;
using JobTracker.Application.Jobs.Queries.GetJobById;
using JobTracker.Application.Jobs.Queries.SearchJobs;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.Registry;
using Xunit;

namespace JobTracker.Tests.Application;

public sealed class RetryBehaviourTests
{
    private sealed record RetryableRequest : IRequest<string>, IRetryableRequest;

    private sealed record PlainRequest : IRequest<string>;

    private sealed class TransientDbException(string message) : DbException(message)
    {
        public override bool IsTransient => true;
    }

    private sealed class PermanentDbException(string message) : DbException(message)
    {
        public override bool IsTransient => false;
    }

    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) =>
            base.CreateTimer(callback, state, TimeSpan.Zero, period);
    }

    private static ServiceProvider BuildProvider(RetryLogCollector? collector = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();

            if (collector is not null)
            {
                builder.AddProvider(collector);
            }
        });

        services.AddSingleton<TimeProvider>(new ImmediateTimeProvider());
        services.AddApplication();

        return services.BuildServiceProvider();
    }

    private static RetryBehaviour<TRequest, string> BehaviourFor<TRequest>(ServiceProvider provider)
        where TRequest : notnull =>
        new(provider.GetRequiredService<ResiliencePipelineProvider<string>>());

    private static async Task<int> CountAttemptsAsync<TRequest>(
        ServiceProvider provider,
        TRequest request,
        Func<int, Task<string>> handler)
        where TRequest : notnull
    {
        var behaviour = BehaviourFor<TRequest>(provider);
        var attempts = 0;

        try
        {
            await behaviour.Handle(
                request,
                _ =>
                {
                    attempts++;
                    return handler(attempts);
                },
                TestContext.Current.CancellationToken);
        }
        catch
        {
            // the attempt count is the assertion
        }

        return attempts;
    }

    [Fact]
    public async Task RetriesUntilTheHandlerSucceeds()
    {
        using var provider = BuildProvider();
        var behaviour = BehaviourFor<RetryableRequest>(provider);
        var attempts = 0;

        var result = await behaviour.Handle(
            new RetryableRequest(),
            _ =>
            {
                attempts++;
                return attempts < 3
                    ? throw new TransientDbException("connection reset")
                    : Task.FromResult("ok");
            },
            TestContext.Current.CancellationToken);

        result.Should().Be("ok");
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task MakesFourAttemptsInTotalBeforeGivingUp()
    {
        using var provider = BuildProvider();

        var attempts = await CountAttemptsAsync(
            provider,
            new RetryableRequest(),
            _ => throw new TransientDbException("connection reset"));

        attempts.Should().Be(4);
    }

    [Fact]
    public async Task RetriesTheLastFailureToTheCaller()
    {
        using var provider = BuildProvider();
        var behaviour = BehaviourFor<RetryableRequest>(provider);

        var act = async () => await behaviour.Handle(
            new RetryableRequest(),
            _ => throw new InvalidOperationException("still broken"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("still broken");
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(TimeoutException))]
    public async Task RetriesAnyExceptionType(Type exceptionType)
    {
        using var provider = BuildProvider();

        var attempts = await CountAttemptsAsync(
            provider,
            new RetryableRequest(),
            _ => throw (Exception)Activator.CreateInstance(exceptionType)!);

        attempts.Should().Be(4);
    }

    [Fact]
    public async Task RetriesADatabaseFailureThatIsNotFlaggedTransient()
    {
        using var provider = BuildProvider();

        var attempts = await CountAttemptsAsync(
            provider,
            new RetryableRequest(),
            _ => throw new PermanentDbException("syntax error"));

        attempts.Should().Be(4);
    }

    [Fact]
    public async Task RetriesAnExceptionNestedInsideAnother()
    {
        using var provider = BuildProvider();
        var behaviour = BehaviourFor<RetryableRequest>(provider);
        var attempts = 0;

        var result = await behaviour.Handle(
            new RetryableRequest(),
            _ =>
            {
                attempts++;
                return attempts < 2
                    ? throw new InvalidOperationException(
                        "save failed",
                        new TransientDbException("deadlock detected"))
                    : Task.FromResult("ok");
            },
            TestContext.Current.CancellationToken);

        result.Should().Be("ok");
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task DoesNotRetryARequestThatDidNotOptIn()
    {
        using var provider = BuildProvider();

        var attempts = await CountAttemptsAsync(
            provider,
            new PlainRequest(),
            _ => throw new TransientDbException("connection reset"));

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task DoesNotRetryACancellationRaisedByTheHandler()
    {
        using var provider = BuildProvider();

        var attempts = await CountAttemptsAsync(
            provider,
            new RetryableRequest(),
            _ => throw new OperationCanceledException());

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task DoesNotRetryACancellationNestedInsideAnother()
    {
        using var provider = BuildProvider();

        var attempts = await CountAttemptsAsync(
            provider,
            new RetryableRequest(),
            _ => throw new InvalidOperationException("aborted", new OperationCanceledException()));

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task DoesNotInvokeTheHandlerWhenTheTokenIsAlreadyCancelled()
    {
        using var provider = BuildProvider();
        var behaviour = BehaviourFor<RetryableRequest>(provider);
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;

        await cancellation.CancelAsync();

        var act = async () => await behaviour.Handle(
            new RetryableRequest(),
            _ =>
            {
                attempts++;
                return Task.FromResult("ok");
            },
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        attempts.Should().Be(0);
    }

    [Fact]
    public async Task PassesTheResponseStraightThroughWhenNothingFails()
    {
        using var provider = BuildProvider();
        var behaviour = BehaviourFor<RetryableRequest>(provider);

        var result = await behaviour.Handle(
            new RetryableRequest(),
            _ => Task.FromResult("first try"),
            TestContext.Current.CancellationToken);

        result.Should().Be("first try");
    }

    [Fact]
    public async Task WaitsOneThenTwoThenFourSeconds()
    {
        var collector = new RetryLogCollector();
        using var provider = BuildProvider(collector);

        await CountAttemptsAsync(
            provider,
            new RetryableRequest(),
            _ => throw new InvalidOperationException("always fails"));

        collector.Delays.Should().Equal(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task LogsTheRequestNameAndAttemptNumberOnEachRetry()
    {
        var collector = new RetryLogCollector();
        using var provider = BuildProvider(collector);

        await CountAttemptsAsync(
            provider,
            new RetryableRequest(),
            _ => throw new InvalidOperationException("always fails"));

        collector.RequestNames.Should().AllBe(nameof(RetryableRequest));
        collector.Attempts.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void QueriesOptIntoRetry()
    {
        typeof(GetJobByIdQuery).Should().BeAssignableTo<IRetryableRequest>();
        typeof(SearchJobsQuery).Should().BeAssignableTo<IRetryableRequest>();
    }

    [Fact]
    public void CommandsDoNotOptIntoRetry()
    {
        typeof(CreateJobCommand).Should().NotBeAssignableTo<IRetryableRequest>();
        typeof(StartJobCommand).Should().NotBeAssignableTo<IRetryableRequest>();
        typeof(CompleteJobCommand).Should().NotBeAssignableTo<IRetryableRequest>();
    }

    [Fact]
    public void BehaviourIsRegisteredInsideTheExceptionLogger()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();

        var behaviours = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        behaviours.Should().ContainInOrder(
            typeof(UnhandledExceptionBehaviour<,>),
            typeof(RetryBehaviour<,>));
    }

    [Fact]
    public void ResiliencePipelineIsRegistered()
    {
        using var provider = BuildProvider();

        var pipeline = provider
            .GetRequiredService<ResiliencePipelineProvider<string>>()
            .GetPipeline(ResiliencePipelines.RequestRetry);

        pipeline.Should().NotBeNull();
    }

    private sealed class RetryLogCollector : ILoggerProvider, ILogger
    {
        private readonly List<TimeSpan> delays = [];
        private readonly List<int> attempts = [];
        private readonly List<string> requestNames = [];

        public IReadOnlyList<TimeSpan> Delays => delays;

        public IReadOnlyList<int> Attempts => attempts;

        public IReadOnlyList<string> RequestNames => requestNames;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return;
            }

            var isRetryLog = values.Any(entry =>
                entry.Key == "{OriginalFormat}" &&
                entry.Value is string template &&
                template.StartsWith("Job Tracker Request:", StringComparison.Ordinal));

            if (!isRetryLog)
            {
                return;
            }

            foreach (var (key, value) in values)
            {
                switch (key)
                {
                    case "Delay" when value is TimeSpan delay:
                        delays.Add(delay);
                        break;
                    case "Attempt" when value is int attempt:
                        attempts.Add(attempt);
                        break;
                    case "Name" when value is string name:
                        requestNames.Add(name);
                        break;
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
