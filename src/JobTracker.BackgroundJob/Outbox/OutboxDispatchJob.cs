using Hangfire;
using JobTracker.Persistence.Outbox;
using Microsoft.Extensions.Options;

namespace JobTracker.BackgroundJob.Outbox;

[AutomaticRetry(Attempts = 0)]
[DisableConcurrentExecution(timeoutInSeconds: 60)]
public sealed class OutboxDispatchJob(IServiceScopeFactory scopeFactory, IOptionsMonitor<OutboxDispatchOptions> options, ILogger<OutboxDispatchJob> logger)
{
    public const string RecurringJobId = "outbox-dispatch";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        var maxBatches = Math.Max(1, current.MaxBatchesPerRun);
        var batchSize = Math.Clamp(current.BatchSize, 1, 500);
        var total = 0;

        for (var batch = 0; batch < maxBatches; batch++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await using var scope = scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<OutboxMessageProcessor>();

            var claimed = await processor.ExecuteAsync(batchSize, cancellationToken);
            total += claimed;

            if (claimed < batchSize)
                break;
        }

        if (total > 0)
        {
            logger.LogInformation("Outbox dispatch claimed {MessageCount} message(s).", total);
        }
    }
}
