using JobTracker.Domain.Common;
using JobTracker.Domain.Enums;
using JobTracker.Domain.Jobs.Events;

namespace JobTracker.Domain.Jobs;

public sealed class Job : AggregateRoot
{
    private readonly List<JobPhoto> photos = [];

    private Job() {}

    private Job(Guid id, string title, string description, Address address, Guid customerId, Guid organizationId, DateTimeOffset createdAtUtc)
        : base(id)
    {
        Title = Required(title, nameof(title), 200);
        Description = Required(description, nameof(description), 4_000);
        Address = address ?? throw new ArgumentNullException(nameof(address));
        CustomerId = RequiredId(customerId, nameof(customerId));
        OrganizationId = RequiredId(organizationId, nameof(organizationId));
        Status = JobStatus.Draft;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        UpdatedAtUtc = CreatedAtUtc;
    }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Address Address { get; private set; } = null!;

    public JobStatus Status { get; private set; }

    public DateTimeOffset? ScheduledDateUtc { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public Guid CustomerId { get; private init; }

    public Guid OrganizationId { get; private init; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public string? SignatureUrl { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<JobPhoto> Photos => photos.AsReadOnly();

    public static Job Create(
        Guid id,
        string title,
        string description,
        Address address,
        Guid customerId,
        Guid organizationId,
        DateTimeOffset occurredOnUtc,
        DateTimeOffset? scheduledDate = null,
        Guid? assigneeId = null)
    {
        var utcNow = occurredOnUtc.ToUniversalTime();
        var job = new Job(id, title, description, address, customerId, organizationId, utcNow);

        if (scheduledDate.HasValue != assigneeId.HasValue)
        {
            throw new ArgumentException("Scheduled date and assignee ID must be supplied together.");
        }

        if (scheduledDate.HasValue && assigneeId.HasValue)
        {
            job.Schedule(scheduledDate.Value, assigneeId.Value, utcNow);
        }

        job.RaiseDomainEvent(new JobCreatedDomainEvent(
            Guid.NewGuid(),
            job.Id,
            job.OrganizationId,
            job.CustomerId,
            job.AssigneeId,
            utcNow));

        return job;
    }

    public void Schedule(DateTimeOffset scheduledDate, Guid assigneeId, DateTimeOffset occurredOnUtc)
    {
        EnsureNotTerminal();

        if (Status != JobStatus.Draft)
        {
            throw new DomainException($"Only a Draft job can be scheduled. Current status: {Status}.");
        }

        var utcNow = occurredOnUtc.ToUniversalTime();
        var scheduledDateUtc = scheduledDate.ToUniversalTime();

        if (scheduledDateUtc <= utcNow)
        {
            throw new DomainException("A job cannot be scheduled in the past.");
        }

        ScheduledDateUtc = scheduledDateUtc;
        AssigneeId = RequiredId(assigneeId, nameof(assigneeId));
        Status = JobStatus.Scheduled;
        UpdatedAtUtc = utcNow;
    }

    public void Start(DateTimeOffset startedAt)
    {
        EnsureNotTerminal();

        if (Status != JobStatus.Scheduled)
        {
            throw new DomainException($"Only a Scheduled job can move to InProgress. Current status: {Status}.");
        }

        if (AssigneeId is null)
        {
            throw new DomainException("A job must have an assignee before it can start.");
        }

        var startedAtUtc = startedAt.ToUniversalTime();

        if (startedAtUtc < CreatedAtUtc)
        {
            throw new DomainException("The job start time cannot be before the job was created.");
        }

        StartedAtUtc = startedAtUtc;
        Status = JobStatus.InProgress;
        UpdatedAtUtc = startedAtUtc;
    }

    public void Complete(DateTimeOffset completedAt, string signatureUrl)
    {
        EnsureNotTerminal();

        if (Status != JobStatus.InProgress)
        {
            throw new DomainException($"Only an InProgress job can be completed. Current status: {Status}.");
        }

        if (StartedAtUtc is null)
        {
            throw new DomainException("A completed job must have a start time.");
        }

        var completedAtUtc = completedAt.ToUniversalTime();

        if (completedAtUtc < StartedAtUtc.Value)
        {
            throw new DomainException("Completion time cannot be before the job start time.");
        }

        SignatureUrl = RequiredAbsoluteUrl(signatureUrl, nameof(signatureUrl));
        CompletedAtUtc = completedAtUtc;
        Status = JobStatus.Completed;
        UpdatedAtUtc = completedAtUtc;

        RaiseDomainEvent(new JobCompletedDomainEvent(
            Guid.NewGuid(),
            Id,
            OrganizationId,
            CustomerId,
            completedAtUtc,
            completedAtUtc));
    }

    public void Cancel(DateTimeOffset cancelledAt, string reason)
    {
        EnsureNotTerminal();

        if (Status is not (JobStatus.Scheduled or JobStatus.InProgress))
        {
            throw new DomainException($"Only a Scheduled or InProgress job can be cancelled. Current status: {Status}.");
        }

        var cancelledAtUtc = cancelledAt.ToUniversalTime();
        var normalizedReason = Required(reason, nameof(reason), 1_000);

        CancelledAtUtc = cancelledAtUtc;
        CancellationReason = normalizedReason;
        Status = JobStatus.Cancelled;
        UpdatedAtUtc = cancelledAtUtc;

        RaiseDomainEvent(new JobCancelledDomainEvent(
            Guid.NewGuid(),
            Id,
            OrganizationId,
            CustomerId,
            cancelledAtUtc,
            normalizedReason,
            cancelledAtUtc));
    }

    public JobPhoto AddPhoto(Guid photoId, string url, DateTimeOffset capturedAt, string? caption)
    {
        EnsureNotTerminal();

        if (Status != JobStatus.InProgress)
        {
            throw new DomainException("Photos can only be added while a job is InProgress.");
        }

        if (photos.Any(photo => photo.Id == photoId))
        {
            throw new DomainException($"Photo '{photoId}' already belongs to this job.");
        }

        var photo = new JobPhoto(photoId, Id, url, capturedAt, caption);
        photos.Add(photo);
        UpdatedAtUtc = capturedAt.ToUniversalTime();

        return photo;
    }

    private void EnsureNotTerminal()
    {
        if (Status is not JobStatus.Completed and not JobStatus.Cancelled)
        {
            return;
        }

        throw new DomainException($"A {Status} job is terminal and cannot transition to another state.");
    }

    private static Guid RequiredId(Guid value, string parameterName)
    {
        return value == Guid.Empty ? throw new ArgumentException("The ID cannot be empty.", parameterName) : value;
    }

    private static string Required(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("The value is required.", parameterName);
        }

        var normalized = value.Trim();

        return normalized.Length > maxLength
            ? throw new ArgumentException($"The value cannot exceed {maxLength} characters.", parameterName)
            : normalized;
    }

    private static string RequiredAbsoluteUrl(string value, string parameterName)
    {
        var normalized = Required(value, parameterName, 2_048);

        return !Uri.TryCreate(normalized, UriKind.Absolute, out _)
            ? throw new ArgumentException("The value must be an absolute URL.", parameterName)
            : normalized;
    }
}
