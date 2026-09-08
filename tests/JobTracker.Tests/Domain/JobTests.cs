using FluentAssertions;
using JobTracker.Domain.Common;
using JobTracker.Domain.Enums;
using JobTracker.Domain.Jobs;
using JobTracker.Domain.Jobs.Events;
using Xunit;

namespace JobTracker.Tests.Domain;

/// <summary>
/// Covers the <see cref="Job"/> status machine (Draft -> Scheduled -> InProgress -> Completed,
/// with Cancelled reachable from Scheduled or InProgress) and the invariants each transition
/// enforces. Photos live in <see cref="JobPhotoTests"/>.
/// </summary>
public class JobTests
{
    /* ---------------------------------------------------------------------------------------
     * Create
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public void Create_WithValidInput_StartsAsDraft()
    {
        var id = Guid.CreateVersion7();
        var customerId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();

        var job = Job.Create(
            id,
            "Fix HVAC",
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            customerId,
            organizationId,
            JobFactory.CreatedAt);

        job.Id.Should().Be(id);
        job.Status.Should().Be(JobStatus.Draft);
        job.CustomerId.Should().Be(customerId);
        job.OrganizationId.Should().Be(organizationId);
        job.CreatedAtUtc.Should().Be(JobFactory.CreatedAt);
        job.UpdatedAtUtc.Should().Be(job.CreatedAtUtc);
        job.ScheduledDateUtc.Should().BeNull();
        job.AssigneeId.Should().BeNull();
        job.StartedAtUtc.Should().BeNull();
        job.CompletedAtUtc.Should().BeNull();
        job.CancelledAtUtc.Should().BeNull();
        job.SignatureUrl.Should().BeNull();
        job.Photos.Should().BeEmpty();
    }

    [Fact]
    public void Create_TrimsTitleAndDescription()
    {
        var job = Job.Create(
            Guid.CreateVersion7(),
            "  Fix HVAC  ",
            "  Replace the failed compressor.  ",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        job.Title.Should().Be("Fix HVAC");
        job.Description.Should().Be("Replace the failed compressor.");
    }

    [Fact]
    public void Create_NormalizesTimestampToUtc()
    {
        // Same instant as CreatedAt, expressed in a +05:00 zone.
        var localCreatedAt = new DateTimeOffset(2026, 1, 5, 14, 0, 0, TimeSpan.FromHours(5));

        var job = JobFactory.Draft(localCreatedAt);

        job.CreatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        job.CreatedAtUtc.Should().Be(JobFactory.CreatedAt);
        job.UpdatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Create_WithScheduleAndAssignee_IsScheduledImmediately()
    {
        var assigneeId = Guid.CreateVersion7();

        var job = Job.Create(
            Guid.CreateVersion7(),
            "Fix HVAC",
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt,
            JobFactory.ScheduledFor,
            assigneeId);

        job.Status.Should().Be(JobStatus.Scheduled);
        job.ScheduledDateUtc.Should().Be(JobFactory.ScheduledFor);
        job.AssigneeId.Should().Be(assigneeId);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Create_WithOnlyOneOfScheduleAndAssignee_Throws(bool hasSchedule, bool hasAssignee)
    {
        var act = () => Job.Create(
            Guid.CreateVersion7(),
            "Fix HVAC",
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt,
            hasSchedule ? JobFactory.ScheduledFor : null,
            hasAssignee ? Guid.CreateVersion7() : null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*must be supplied together*");
    }

    [Fact]
    public void Create_WithScheduleInThePast_Throws()
    {
        var act = () => Job.Create(
            Guid.CreateVersion7(),
            "Fix HVAC",
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt,
            JobFactory.CreatedAt.AddDays(-1),
            Guid.CreateVersion7());

        act.Should().Throw<DomainException>()
            .WithMessage("*cannot be scheduled in the past*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithBlankTitle_Throws(string title)
    {
        var act = () => Job.Create(
            Guid.CreateVersion7(),
            title,
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("title");
    }

    [Fact]
    public void Create_WithTitleOverMaxLength_Throws()
    {
        var act = () => Job.Create(
            Guid.CreateVersion7(),
            new string('a', 201),
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot exceed 200 characters*")
            .WithParameterName("title");
    }

    [Fact]
    public void Create_WithDescriptionOverMaxLength_Throws()
    {
        var act = () => Job.Create(
            Guid.CreateVersion7(),
            "Fix HVAC",
            new string('a', 4_001),
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("description");
    }

    [Fact]
    public void Create_WithNullAddress_Throws()
    {
        var act = () => Job.Create(
            Guid.CreateVersion7(),
            "Fix HVAC",
            "Replace the failed compressor.",
            null!,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("address");
    }

    [Fact]
    public void Create_WithEmptyId_Throws()
    {
        var act = () => Job.Create(
            Guid.Empty,
            "Fix HVAC",
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("id");
    }

    [Fact]
    public void Create_WithEmptyCustomerId_Throws()
    {
        var act = () => Job.Create(
            Guid.CreateVersion7(),
            "Fix HVAC",
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.Empty,
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("customerId");
    }

    /// <summary>
    /// OrganizationId is the only tenant boundary in the system, so an empty one must never
    /// reach the database.
    /// </summary>
    [Fact]
    public void Create_WithEmptyOrganizationId_Throws()
    {
        var act = () => Job.Create(
            Guid.CreateVersion7(),
            "Fix HVAC",
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.Empty,
            JobFactory.CreatedAt);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("organizationId");
    }

    /* ---------------------------------------------------------------------------------------
     * Schedule
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public void Schedule_FromDraft_MovesToScheduled()
    {
        var job = JobFactory.Draft();
        var assigneeId = Guid.CreateVersion7();

        job.Schedule(JobFactory.ScheduledFor, assigneeId, JobFactory.CreatedAt);

        job.Status.Should().Be(JobStatus.Scheduled);
        job.ScheduledDateUtc.Should().Be(JobFactory.ScheduledFor);
        job.AssigneeId.Should().Be(assigneeId);
        job.UpdatedAtUtc.Should().Be(JobFactory.CreatedAt);
    }

    [Fact]
    public void Schedule_NormalizesScheduledDateToUtc()
    {
        var job = JobFactory.Draft();

        // Same instant as ScheduledFor, expressed in a +05:00 zone.
        var localSchedule = new DateTimeOffset(2026, 1, 6, 14, 0, 0, TimeSpan.FromHours(5));

        job.Schedule(localSchedule, Guid.CreateVersion7(), JobFactory.CreatedAt);

        job.ScheduledDateUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        job.ScheduledDateUtc.Should().Be(JobFactory.ScheduledFor);
    }

    [Fact]
    public void Schedule_InThePast_Throws()
    {
        var job = JobFactory.Draft();

        var act = () => job.Schedule(
            JobFactory.CreatedAt.AddMinutes(-1),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<DomainException>()
            .WithMessage("*cannot be scheduled in the past*");
    }

    /// <summary>The boundary is exclusive: <c>scheduledDateUtc &lt;= utcNow</c> is rejected.</summary>
    [Fact]
    public void Schedule_AtExactlyNow_Throws()
    {
        var job = JobFactory.Draft();

        var act = () => job.Schedule(
            JobFactory.CreatedAt,
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<DomainException>()
            .WithMessage("*cannot be scheduled in the past*");
    }

    [Fact]
    public void Schedule_WhenAlreadyScheduled_Throws()
    {
        var job = JobFactory.Scheduled();

        var act = () => job.Schedule(
            JobFactory.ScheduledFor.AddDays(1),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt);

        act.Should().Throw<DomainException>()
            .WithMessage("*Only a Draft job can be scheduled*");
    }

    [Fact]
    public void Schedule_WithEmptyAssignee_Throws()
    {
        var job = JobFactory.Draft();

        var act = () => job.Schedule(JobFactory.ScheduledFor, Guid.Empty, JobFactory.CreatedAt);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("assigneeId");
    }

    /* ---------------------------------------------------------------------------------------
     * Start
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public void Start_FromScheduled_MovesToInProgress()
    {
        var job = JobFactory.Scheduled();

        job.Start(JobFactory.StartedAt);

        job.Status.Should().Be(JobStatus.InProgress);
        job.StartedAtUtc.Should().Be(JobFactory.StartedAt);
        job.UpdatedAtUtc.Should().Be(JobFactory.StartedAt);
    }

    [Fact]
    public void Start_NormalizesStartedAtToUtc()
    {
        var job = JobFactory.Scheduled();

        // Same instant as StartedAt, expressed in a +05:00 zone.
        var localStart = new DateTimeOffset(2026, 1, 6, 14, 5, 0, TimeSpan.FromHours(5));

        job.Start(localStart);

        job.StartedAtUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
        job.StartedAtUtc.Should().Be(JobFactory.StartedAt);
    }

    [Fact]
    public void Start_FromDraft_Throws()
    {
        var job = JobFactory.Draft();

        var act = () => job.Start(JobFactory.StartedAt);

        act.Should().Throw<DomainException>()
            .WithMessage("*Only a Scheduled job can move to InProgress*");
    }

    [Fact]
    public void Start_BeforeTheJobWasCreated_Throws()
    {
        var job = JobFactory.Scheduled();

        var act = () => job.Start(JobFactory.CreatedAt.AddMinutes(-1));

        act.Should().Throw<DomainException>()
            .WithMessage("*cannot be before the job was created*");
    }

    /* ---------------------------------------------------------------------------------------
     * Complete
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public void Complete_FromInProgress_MovesToCompleted()
    {
        var job = JobFactory.InProgress();

        job.Complete(JobFactory.CompletedAt, JobFactory.SignatureUrl);

        job.Status.Should().Be(JobStatus.Completed);
        job.CompletedAtUtc.Should().Be(JobFactory.CompletedAt);
        job.SignatureUrl.Should().Be(JobFactory.SignatureUrl);
        job.UpdatedAtUtc.Should().Be(JobFactory.CompletedAt);
    }

    [Fact]
    public void Complete_AtExactlyTheStartTime_IsAllowed()
    {
        var job = JobFactory.InProgress();

        job.Complete(JobFactory.StartedAt, JobFactory.SignatureUrl);

        job.Status.Should().Be(JobStatus.Completed);
        job.CompletedAtUtc.Should().Be(JobFactory.StartedAt);
    }

    [Fact]
    public void Complete_BeforeTheStartTime_Throws()
    {
        var job = JobFactory.InProgress();

        var act = () => job.Complete(JobFactory.StartedAt.AddMinutes(-1), JobFactory.SignatureUrl);

        act.Should().Throw<DomainException>()
            .WithMessage("*cannot be before the job start time*");
    }

    [Fact]
    public void Complete_FromScheduled_Throws()
    {
        var job = JobFactory.Scheduled();

        var act = () => job.Complete(JobFactory.CompletedAt, JobFactory.SignatureUrl);

        act.Should().Throw<DomainException>()
            .WithMessage("*Only an InProgress job can be completed*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/signatures/abc.png")]
    public void Complete_WithoutAnAbsoluteSignatureUrl_Throws(string signatureUrl)
    {
        var job = JobFactory.InProgress();

        var act = () => job.Complete(JobFactory.CompletedAt, signatureUrl);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("signatureUrl");
    }

    [Fact]
    public void Complete_WithSignatureUrlOverMaxLength_Throws()
    {
        var job = JobFactory.InProgress();
        var longUrl = "https://example.com/" + new string('a', 2_048);

        var act = () => job.Complete(JobFactory.CompletedAt, longUrl);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot exceed 2048 characters*")
            .WithParameterName("signatureUrl");
    }

    /// <summary>
    /// The signature is validated before any field is written, so a rejected completion leaves
    /// the aggregate untouched rather than half-applied.
    /// </summary>
    [Fact]
    public void Complete_WithInvalidSignatureUrl_LeavesTheJobUnchanged()
    {
        var job = JobFactory.InProgress();

        var act = () => job.Complete(JobFactory.CompletedAt, "not-a-url");

        act.Should().Throw<ArgumentException>();
        job.Status.Should().Be(JobStatus.InProgress);
        job.CompletedAtUtc.Should().BeNull();
        job.SignatureUrl.Should().BeNull();
        job.UpdatedAtUtc.Should().Be(JobFactory.StartedAt);
    }

    /* ---------------------------------------------------------------------------------------
     * Cancel
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public void Cancel_FromScheduled_MovesToCancelled()
    {
        var job = JobFactory.Scheduled();

        job.Cancel(JobFactory.ScheduledFor, "  Customer called it off.  ");

        job.Status.Should().Be(JobStatus.Cancelled);
        job.CancelledAtUtc.Should().Be(JobFactory.ScheduledFor);
        job.CancellationReason.Should().Be("Customer called it off.");
        job.UpdatedAtUtc.Should().Be(JobFactory.ScheduledFor);
    }

    [Fact]
    public void Cancel_FromInProgress_MovesToCancelled()
    {
        var job = JobFactory.InProgress();

        job.Cancel(JobFactory.CompletedAt, "Parts unavailable.");

        job.Status.Should().Be(JobStatus.Cancelled);
        job.CancelledAtUtc.Should().Be(JobFactory.CompletedAt);
    }

    [Fact]
    public void Cancel_FromDraft_Throws()
    {
        var job = JobFactory.Draft();

        var act = () => job.Cancel(JobFactory.CreatedAt, "Customer called it off.");

        act.Should().Throw<DomainException>()
            .WithMessage("*Only a Scheduled or InProgress job can be cancelled*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithBlankReason_Throws(string reason)
    {
        var job = JobFactory.Scheduled();

        var act = () => job.Cancel(JobFactory.ScheduledFor, reason);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("reason");
    }

    [Fact]
    public void Cancel_WithReasonOverMaxLength_Throws()
    {
        var job = JobFactory.Scheduled();

        var act = () => job.Cancel(JobFactory.ScheduledFor, new string('a', 1_001));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot exceed 1000 characters*")
            .WithParameterName("reason");
    }

    /* ---------------------------------------------------------------------------------------
     * Terminal states
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public void Completed_IsTerminal()
    {
        AssertTerminal(JobFactory.Completed(), JobStatus.Completed);
    }

    [Fact]
    public void Cancelled_IsTerminal()
    {
        AssertTerminal(JobFactory.Cancelled(), JobStatus.Cancelled);
    }

    private static void AssertTerminal(Job job, JobStatus expectedStatus)
    {
        var later = JobFactory.CompletedAt.AddDays(1);

        var transitions = new List<Action>
        {
            () => job.Schedule(later, Guid.CreateVersion7(), later),
            () => job.Start(later),
            () => job.Complete(later, JobFactory.SignatureUrl),
            () => job.Cancel(later, "Too late."),
            () => job.AddPhoto(Guid.CreateVersion7(), JobFactory.PhotoUrl, later, null),
        };

        foreach (var transition in transitions)
        {
            transition.Should().Throw<DomainException>()
                .WithMessage($"*A {expectedStatus} job is terminal*");
        }

        job.Status.Should().Be(expectedStatus);
    }

    /* ---------------------------------------------------------------------------------------
     * Domain events
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public void Create_RaisesJobCreatedDomainEvent()
    {
        var job = JobFactory.Draft();

        var created = job.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<JobCreatedDomainEvent>().Subject;

        created.JobId.Should().Be(job.Id);
        created.OrganizationId.Should().Be(job.OrganizationId);
        created.CustomerId.Should().Be(job.CustomerId);
        created.AssigneeId.Should().BeNull();
        created.OccurredOnUtc.Should().Be(JobFactory.CreatedAt);
        created.EventId.Should().NotBeEmpty();
    }

    /// <summary>
    /// Scheduling inside <c>Create</c> happens before the created event is raised, so the event
    /// carries the assignee rather than null.
    /// </summary>
    [Fact]
    public void Create_WithSchedule_RaisesOneEventCarryingTheAssignee()
    {
        var assigneeId = Guid.CreateVersion7();

        var job = Job.Create(
            Guid.CreateVersion7(),
            "Fix HVAC",
            "Replace the failed compressor.",
            JobFactory.AnAddress(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            JobFactory.CreatedAt,
            JobFactory.ScheduledFor,
            assigneeId);

        job.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<JobCreatedDomainEvent>()
            .Which.AssigneeId.Should().Be(assigneeId);
    }

    [Fact]
    public void Schedule_RaisesNoDomainEvent()
    {
        var job = JobFactory.Draft();
        job.ClearDomainEvents();

        job.Schedule(JobFactory.ScheduledFor, Guid.CreateVersion7(), JobFactory.CreatedAt);

        job.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Start_RaisesNoDomainEvent()
    {
        var job = JobFactory.Scheduled();
        job.ClearDomainEvents();

        job.Start(JobFactory.StartedAt);

        job.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Complete_RaisesJobCompletedDomainEvent()
    {
        var job = JobFactory.InProgress();
        job.ClearDomainEvents();

        job.Complete(JobFactory.CompletedAt, JobFactory.SignatureUrl);

        var completed = job.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<JobCompletedDomainEvent>().Subject;

        completed.JobId.Should().Be(job.Id);
        completed.OrganizationId.Should().Be(job.OrganizationId);
        completed.CustomerId.Should().Be(job.CustomerId);
        completed.CompletedAtUtc.Should().Be(JobFactory.CompletedAt);
        completed.OccurredOnUtc.Should().Be(JobFactory.CompletedAt);
    }

    [Fact]
    public void Cancel_RaisesJobCancelledDomainEventCarryingTheTrimmedReason()
    {
        var job = JobFactory.Scheduled();
        job.ClearDomainEvents();

        job.Cancel(JobFactory.ScheduledFor, "  Customer called it off.  ");

        var cancelled = job.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<JobCancelledDomainEvent>().Subject;

        cancelled.JobId.Should().Be(job.Id);
        cancelled.Reason.Should().Be("Customer called it off.");
        cancelled.CancelledAtUtc.Should().Be(JobFactory.ScheduledFor);
    }

    [Fact]
    public void DomainEvents_AccumulateAcrossTransitions()
    {
        var job = JobFactory.Completed();

        job.DomainEvents.Should().HaveCount(2);
        job.DomainEvents.Should().ContainItemsAssignableTo<IDomainEvent>();
        job.DomainEvents.OfType<JobCreatedDomainEvent>().Should().ContainSingle();
        job.DomainEvents.OfType<JobCompletedDomainEvent>().Should().ContainSingle();
    }

    /// <summary>
    /// <c>InsertOutboxMessagesInterceptor</c> removes each event it maps rather than clearing the
    /// whole collection, so single removal has to leave the rest in place.
    /// </summary>
    [Fact]
    public void RemoveDomainEvent_RemovesOnlyThatEvent()
    {
        var job = JobFactory.Completed();
        var completed = job.DomainEvents.OfType<JobCompletedDomainEvent>().Single();

        job.RemoveDomainEvent(completed);

        job.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<JobCreatedDomainEvent>();
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheCollection()
    {
        var job = JobFactory.Completed();

        job.ClearDomainEvents();

        job.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void RemoveDomainEvent_WithNull_Throws()
    {
        var job = JobFactory.Draft();

        var act = () => job.RemoveDomainEvent(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
