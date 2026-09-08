using MediatR;
using Polly;
using Polly.Registry;

namespace JobTracker.Application.Common.Behaviours;

public sealed class RetryBehaviour<TRequest, TResponse>(ResiliencePipelineProvider<string> pipelineProvider) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,CancellationToken cancellationToken)
    {
        if (request is not IRetryableRequest)
        {
            return await next(cancellationToken);
        }

        var pipeline = pipelineProvider.GetPipeline(ResiliencePipelines.RequestRetry);
        var context = ResilienceContextPool.Shared.Get(cancellationToken);
        context.Properties.Set(ResiliencePipelines.RequestNameKey, typeof(TRequest).Name);

        try
        {
            return await pipeline.ExecuteAsync(
                static async (resilienceContext, state) => await state(resilienceContext.CancellationToken),
                context,
                next);
        }
        finally
        {
            ResilienceContextPool.Shared.Return(context);
        }
    }
}

public static class ResiliencePipelines
{
    public const string RequestRetry = "jobtracker-request-retry";

    public static readonly ResiliencePropertyKey<string> RequestNameKey = new("RequestName");

    public static bool ShouldRetry(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException)
            {
                return false;
            }
        }

        return exception is not null;
    }
}
