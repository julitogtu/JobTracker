namespace JobTracker.Persistence.Outbox;

internal sealed class OutboxMessage
{
    private OutboxMessage() { }

    private OutboxMessage(Guid id, string type, string content, DateTimeOffset occurredOnUtc, string correlationId)
    {
        Id = id;
        Type = type;
        Content = content;
        OccurredOnUtc = occurredOnUtc.ToUniversalTime();
        NextAttemptOnUtc = OccurredOnUtc;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private init; }

    public string Type { get; private init; } = string.Empty;

    public string Content { get; private init; } = string.Empty;

    public DateTimeOffset OccurredOnUtc { get; private init; }

    public string CorrelationId { get; private init; } = string.Empty;

    public DateTimeOffset? ProcessedOnUtc { get; private set; }

    public int RetryCount { get; private set; }

    public DateTimeOffset NextAttemptOnUtc { get; private set; }

    public string? LastError { get; private set; }

    public Guid? LockId { get; private set; }

    public DateTimeOffset? LockedUntilUtc { get; private set; }

    public static OutboxMessage Create(
        Guid id,
        string type,
        string content,
        DateTimeOffset occurredOnUtc,
        string correlationId) =>
        new(id, type, content, occurredOnUtc, correlationId);

    public void Claim(Guid lockId, DateTimeOffset lockedUntilUtc)
    {
        LockId = lockId;
        LockedUntilUtc = lockedUntilUtc.ToUniversalTime();
    }

    public void MarkProcessed(DateTimeOffset processedOnUtc)
    {
        ProcessedOnUtc = processedOnUtc.ToUniversalTime();
        LastError = null;
        LockId = null;
        LockedUntilUtc = null;
    }

    public void MarkFailed(string error, DateTimeOffset nextAttemptOnUtc)
    {
        RetryCount++;
        LastError = error.Length <= 4_000 ? error : error[..4_000];
        NextAttemptOnUtc = nextAttemptOnUtc.ToUniversalTime();
        LockId = null;
        LockedUntilUtc = null;
    }
}
