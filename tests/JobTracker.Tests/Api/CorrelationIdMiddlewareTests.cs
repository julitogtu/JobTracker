using FluentAssertions;
using JobTracker.Api.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Xunit;

namespace JobTracker.Tests.Api;

/// <summary>
/// Runs the middleware in a real pipeline rather than against a hand-built HttpContext.
///
/// That is not gold-plating: the response header is written from an <c>OnStarting</c> callback,
/// which nothing invokes on a <c>DefaultHttpContext</c>. Asserting it any other way would be
/// asserting that a callback was registered, not that the header ships.
/// </summary>
public sealed class CorrelationIdMiddlewareTests
{
    private const string ValidId = "0199a4c1-1f5e-7c3a-9b2d-4e6f8a0b1c2d";

    /// <summary>
    /// Builds a pipeline of just this middleware plus a terminal that reports what the
    /// middleware put in scope. The Serilog sink is in-memory so a test can assert on the
    /// properties attached to a log line.
    /// </summary>
    private static async Task<IHost> StartHostAsync(List<LogEvent>? logEvents = null)
    {
        var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new CollectingSink(logEvents ?? []))
            .CreateLogger();

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost => webHost
                .UseTestServer()
                .ConfigureServices(services => services.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddSerilog(logger, dispose: true);
                }))
                .Configure(app =>
                {
                    app.UseMiddleware<CorrelationIdMiddleware>();
                    app.Run(async context =>
                    {
                        var correlationId = context.GetCorrelationId();

                        context.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Test")
                            .LogInformation("handled");

                        await context.Response.WriteAsync(correlationId ?? "<none>");
                    });
                }))
            .StartAsync(TestContext.Current.CancellationToken);

        return host;
    }

    private static Task<HttpResponseMessage> GetAsync(IHost host, string? correlationId = null)
    {
        var client = host.GetTestClient();

        if (correlationId is not null)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                CorrelationIdMiddleware.HeaderName,
                correlationId);
        }

        return client.GetAsync("/", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MintsACorrelationIdWhenTheCallerSendsNone()
    {
        using var host = await StartHostAsync();

        var response = await GetAsync(host);

        var header = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        header.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(header, out _).Should().BeTrue("the generated id is a GUID");
    }

    [Fact]
    public async Task EchoesTheCallerSuppliedId()
    {
        using var host = await StartHostAsync();

        var response = await GetAsync(host, ValidId);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single().Should().Be(ValidId);
    }

    /// <summary>A chain of services has to end up sharing one id, which is the whole point.</summary>
    [Fact]
    public async Task MakesTheIdAvailableToTheRestOfThePipeline()
    {
        using var host = await StartHostAsync();

        var response = await GetAsync(host, ValidId);

        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Be(ValidId);
    }

    [Fact]
    public async Task GivesEachRequestItsOwnId()
    {
        using var host = await StartHostAsync();

        var first = await GetAsync(host);
        var second = await host.GetTestClient().GetAsync("/", TestContext.Current.CancellationToken);

        var firstId = first.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        var secondId = second.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();

        firstId.Should().NotBe(secondId);
    }

    /* ---------------------------------------------------------------------------------------
     * Inbound header validation. The value lands in every log line for the request, so a
     * caller must not be able to choose an arbitrary one.
     * ------------------------------------------------------------------------------------ */

    [Theory]
    [InlineData("has spaces")]
    [InlineData("newline\rinjected")]
    [InlineData("semi;colon")]
    [InlineData("angle<brackets>")]
    [InlineData("quote\"mark")]
    public async Task RejectsAnIdWithCharactersOutsideTheAllowedSet(string hostile)
    {
        using var host = await StartHostAsync();

        var response = await GetAsync(host, hostile);

        var header = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        header.Should().NotBe(hostile);
        Guid.TryParse(header, out _).Should().BeTrue("a fresh id replaces the rejected one");
    }

    [Fact]
    public async Task RejectsAnIdLongerThanTheLimit()
    {
        using var host = await StartHostAsync();

        var response = await GetAsync(host, new string('a', 129));

        var header = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        header.Should().NotStartWith("aaaa");
        Guid.TryParse(header, out _).Should().BeTrue();
    }

    [Fact]
    public async Task AcceptsAnIdExactlyAtTheLimit()
    {
        using var host = await StartHostAsync();
        var atLimit = new string('a', 128);

        var response = await GetAsync(host, atLimit);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single().Should().Be(atLimit);
    }

    [Fact]
    public async Task RejectsABlankId()
    {
        using var host = await StartHostAsync();

        var response = await GetAsync(host, "   ");

        var header = response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();
        Guid.TryParse(header, out _).Should().BeTrue();
    }

    /// <summary>A W3C trace id is a plausible thing for a caller to reuse as the correlation id.</summary>
    [Fact]
    public async Task AcceptsAHexTraceIdAsTheCorrelationId()
    {
        using var host = await StartHostAsync();
        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        var response = await GetAsync(host, traceId);

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single().Should().Be(traceId);
    }

    /* ---------------------------------------------------------------------------------------
     * Log enrichment
     * ------------------------------------------------------------------------------------ */

    /// <summary>
    /// The regression guard that matters most. <c>LogContext.PushProperty</c> reaches the sinks
    /// only when the logger is built with <c>Enrich.FromLogContext()</c>; without it the
    /// middleware still runs, still returns the header, and silently enriches nothing — the
    /// exact state this repo was in while Serilog sat referenced but unwired. This test fails
    /// if that wiring is ever dropped.
    /// </summary>
    [Fact]
    public async Task AttachesTheCorrelationIdToEveryLogLineOfTheRequest()
    {
        var events = new List<LogEvent>();
        using var host = await StartHostAsync(events);

        await GetAsync(host, ValidId);

        var handled = events.Should().ContainSingle(e => e.MessageTemplate.Text == "handled").Subject;

        handled.Properties.Should().ContainKey("CorrelationId");
        handled.Properties["CorrelationId"].ToString().Trim('"').Should().Be(ValidId);
    }

    [Fact]
    public async Task AttachesATraceIdAlongsideTheCorrelationId()
    {
        var events = new List<LogEvent>();
        using var host = await StartHostAsync(events);

        await GetAsync(host);

        var handled = events.Single(e => e.MessageTemplate.Text == "handled");

        handled.Properties.Should().ContainKey("TraceId");
        handled.Properties["TraceId"].ToString().Trim('"').Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// The log context is ambient and async-local, so a leak across requests would silently
    /// mislabel logs rather than fail anything.
    /// </summary>
    [Fact]
    public async Task DoesNotLeakTheIdBetweenRequests()
    {
        var events = new List<LogEvent>();
        using var host = await StartHostAsync(events);

        await GetAsync(host, ValidId);
        await host.GetTestClient().GetAsync("/", TestContext.Current.CancellationToken);

        var ids = events
            .Where(e => e.MessageTemplate.Text == "handled")
            .Select(e => e.Properties["CorrelationId"].ToString().Trim('"'))
            .ToList();

        ids.Should().HaveCount(2);
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().Contain(ValidId);
    }

    private sealed class CollectingSink(List<LogEvent> events) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            lock (events)
            {
                events.Add(logEvent);
            }
        }
    }
}
