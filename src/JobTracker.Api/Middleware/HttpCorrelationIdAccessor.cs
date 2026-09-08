using JobTracker.Application.Common.Correlation;

namespace JobTracker.Api.Middleware;

internal sealed class HttpCorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    : ICorrelationIdAccessor
{
    public string CorrelationId =>
        httpContextAccessor.HttpContext?.GetCorrelationId() ?? ICorrelationIdAccessor.None;
}
