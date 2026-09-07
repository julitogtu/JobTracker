namespace JobTracker.Application.Common.Messaging;

public interface IOutboxMessagePublisher
{
    Task PublishAsync(
        OutboxEnvelope message,
        CancellationToken cancellationToken = default);
}
