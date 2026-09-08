using System.Diagnostics;
using Serilog.Context;

namespace JobTracker.Api.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public const string ItemKey = "CorrelationId";

    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadInbound(context.Request) ?? Guid.CreateVersion7().ToString();

        context.Items[ItemKey] = correlationId;

        context.Response.OnStarting(
            static state =>
            {
                var httpContext = (HttpContext)state;
                httpContext.Response.Headers[HeaderName] = (string)httpContext.Items[ItemKey]!;

                return Task.CompletedTask;
            },
            context);

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        using (LogContext.PushProperty(ItemKey, correlationId))
        using (LogContext.PushProperty("TraceId", traceId))
        {
            await next(context);
        }
    }

    private static string? ReadInbound(HttpRequest request)
    {
        var candidate = request.Headers[HeaderName].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > MaxLength)
        {
            return null;
        }

        foreach (var character in candidate)
        {
            if (!IsAllowed(character))
            {
                return null;
            }
        }

        return candidate;
    }

    private static bool IsAllowed(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':';
}
