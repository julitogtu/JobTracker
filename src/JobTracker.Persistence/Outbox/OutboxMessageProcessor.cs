using JobTracker.Application.Common.Messaging;
using JobTracker.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobTracker.Persistence.Outbox;

public sealed class OutboxMessageProcessor(
    JobsDbContext dbContext,
    IOutboxMessagePublisher publisher,
    TimeProvider timeProvider,
    ILogger<OutboxMessageProcessor> logger)
{
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(2);

    public async Task ExecuteAsync(
        int batchSize = 50,
        CancellationToken cancellationToken = default)
    {
        batchSize = Math.Clamp(batchSize, 1, 500);
        var lockId = Guid.NewGuid();
        var messages = await ClaimBatchAsync(lockId, batchSize, cancellationToken);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await publisher.PublishAsync(
                    new OutboxEnvelope(
                        message.Id,
                        message.Type,
                        message.Content,
                        message.OccurredOnUtc),
                    cancellationToken);

                message.MarkProcessed(timeProvider.GetUtcNow());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                var nextAttempt = timeProvider.GetUtcNow() + CalculateBackoff(message.RetryCount + 1);
                message.MarkFailed(exception.ToString(), nextAttempt);
                logger.LogError(
                    exception,
                    "Outbox message {MessageId} failed. Retry {RetryCount} is scheduled for {NextAttempt}.",
                    message.Id,
                    message.RetryCount,
                    nextAttempt);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<List<OutboxMessage>> ClaimBatchAsync(
        Guid lockId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var lockedUntil = now + LockDuration;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var messages = await dbContext.OutboxMessages
            .FromSqlInterpolated($$"""
                SELECT *
                FROM jobs.outbox_messages
                WHERE processed_on_utc IS NULL
                  AND next_attempt_on_utc <= {{now}}
                  AND (locked_until_utc IS NULL OR locked_until_utc < {{now}})
                ORDER BY occurred_on_utc, id
                FOR UPDATE SKIP LOCKED
                LIMIT {{batchSize}}
                """)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            message.Claim(lockId, lockedUntil);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return messages;
    }

    private static TimeSpan CalculateBackoff(int retryCount)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Min(retryCount, 8)));
        return TimeSpan.FromSeconds(seconds);
    }
}
