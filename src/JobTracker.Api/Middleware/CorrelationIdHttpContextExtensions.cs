namespace JobTracker.Api.Middleware;

public static class CorrelationIdHttpContextExtensions
{
    public static string? GetCorrelationId(this HttpContext context) =>
        context.Items[CorrelationIdMiddleware.ItemKey] as string;
}
