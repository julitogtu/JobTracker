namespace JobTracker.Application.Common.Messaging;

public sealed record OutboxEnvelope(
    Guid Id,
    string Type,
    string Content,
    DateTimeOffset OccurredOnUtc);
