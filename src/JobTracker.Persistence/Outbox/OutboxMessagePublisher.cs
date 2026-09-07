using JobTracker.Application.Common.Messaging;

namespace JobTracker.Persistence.Outbox;

public sealed class OutboxMessagePublisher : IOutboxMessagePublisher
{
    public Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}