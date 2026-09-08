using FluentValidation;
using JobTracker.Application.Common.Behaviours;
using MediatR;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using System.Reflection;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
        });

        services.AddResiliencePipeline(ResiliencePipelines.RequestRetry, (builder, context) =>
        {
            var logger = context.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger(ResiliencePipelines.RequestRetry);

            builder.TimeProvider = context.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = args => ValueTask.FromResult(
                    ResiliencePipelines.ShouldRetry(args.Outcome.Exception)),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(4),
                UseJitter = false,
                OnRetry = args =>
                {
                    args.Context.Properties.TryGetValue(
                        ResiliencePipelines.RequestNameKey,
                        out var requestName);

                    logger.LogWarning(
                        args.Outcome.Exception,
                        "Job Tracker Request: {Name} failed. Retry {Attempt} in {Delay}.",
                        requestName ?? "unknown",
                        args.AttemptNumber + 1,
                        args.RetryDelay);

                    return ValueTask.CompletedTask;
                }
            });
        });

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehaviour<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehaviour<,>));

        return services;
    }
}
