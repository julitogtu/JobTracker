using MediatR;
using Microsoft.Extensions.Logging;

namespace JobTracker.Application.Common.Behaviours;

public class RetryBehaviour<TRequest, TResponse>(ILogger<TRequest> logger) : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        try
        {
            return await next();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Job Tracker Request: Exception {Name} {@Request}", requestName, request);
            // Implement retry logic here if needed
            throw;
        }
    }
}
