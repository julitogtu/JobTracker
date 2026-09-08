using FluentAssertions;
using JobTracker.Application.Common.Results;
using JobTracker.Application.Jobs.Commands.CompleteJob;
using JobTracker.Application.Jobs.Commands.CreateJob;
using JobTracker.Application.Jobs.Commands.StartJob;
using JobTracker.Application.Jobs.Queries.GetJobById;
using JobTracker.Domain.Enums;
using JobTracker.Domain.Jobs;
using Xunit;

namespace JobTracker.Tests.Integration;

/// <summary>
/// The full request path a controller triggers: <c>Mediator.Send</c> → handler → repository →
/// database, with the real MediatR pipeline and the real DI container. What is under test here
/// is the seam the unit tests cannot see — that a handler's Result carries the ErrorType the
/// controller needs, and that the write it claims to have made is actually on disk.
/// </summary>
public sealed class JobPipelineTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private CreateJobCommand ACreateCommand(
        string title = "Fix HVAC",
        string description = "Replace the failed compressor.",
        DateTimeOffset? scheduledDateUtc = null,
        Guid? assigneeId = null) =>
        new(
            Title: title,
            Description: description,
            Street: "123 Main St",
            City: "Austin",
            State: "TX",
            ZipCode: "78701",
            Latitude: 30.267200m,
            Longitude: -97.743100m,
            CustomerId: Guid.CreateVersion7(),
            OrganizationId: OrganizationId,
            ScheduledDateUtc: scheduledDateUtc,
            AssigneeId: assigneeId);

    /* ---------------------------------------------------------------------------------------
     * CreateJob
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task CreateJob_PersistsADraftAndReturnsItsId()
    {
        var result = await Send(ACreateCommand());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();

        Db.ChangeTracker.Clear();
        var saved = await GetJobAsync(OrganizationId, result.Value);

        saved.Should().NotBeNull();
        saved!.Status.Should().Be(JobStatus.Draft);
        saved.Title.Should().Be("Fix HVAC");
        saved.CreatedAtUtc.Should().Be(Now, "the handler takes its clock from the injected TimeProvider");
    }

    [Fact]
    public async Task CreateJob_WithScheduleAndAssignee_PersistsAsScheduled()
    {
        var assigneeId = Guid.CreateVersion7();

        var result = await Send(ACreateCommand(
            scheduledDateUtc: Now.AddDays(2),
            assigneeId: assigneeId));

        Db.ChangeTracker.Clear();
        var saved = await GetJobAsync(OrganizationId, result.Value);

        saved!.Status.Should().Be(JobStatus.Scheduled);
        saved.ScheduledDateUtc.Should().Be(Now.AddDays(2));
        saved.AssigneeId.Should().Be(assigneeId);
    }

    [Fact]
    public async Task CreateJob_WithABlankTitle_ReturnsAValidationError()
    {
        var result = await Send(ACreateCommand(title: "   "));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Jobs.Create.InvalidInput");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    /// <summary>
    /// A past scheduled date is a domain rule, not an input-shape rule, so it comes back as
    /// <c>Jobs.Create.Invalid</c> from the DomainException branch rather than InvalidInput.
    /// </summary>
    [Fact]
    public async Task CreateJob_WithAPastScheduledDate_ReturnsTheDomainErrorCode()
    {
        var result = await Send(ACreateCommand(
            scheduledDateUtc: Now.AddDays(-1),
            assigneeId: Guid.CreateVersion7()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Jobs.Create.Invalid");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task CreateJob_WithAScheduleButNoAssignee_ReturnsAValidationError()
    {
        var result = await Send(ACreateCommand(scheduledDateUtc: Now.AddDays(2)));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Jobs.Create.InvalidInput");
    }

    [Fact]
    public async Task CreateJob_WhenItFails_WritesNothing()
    {
        await Send(ACreateCommand(title: "   "));

        Db.ChangeTracker.Clear();
        var page = await SearchAsync(new JobSearchCriteria(OrganizationId));

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    /// FluentValidation validators are registered but no pipeline behaviour runs them, so a
    /// blank title reaches the aggregate and fails there. This pins the documented gap: if a
    /// ValidationBehaviour is ever added, this test is what tells you the error shape changed.
    /// </summary>
    [Fact]
    public async Task CreateJob_InvalidInput_IsRejectedByTheAggregate_NotAValidationBehaviour()
    {
        var result = await Send(ACreateCommand(title: "   "));

        // An ArgumentException from Job.Create, surfaced by the handler's own catch.
        result.Error.Description.Should().Contain("required");
    }

    /* ---------------------------------------------------------------------------------------
     * StartJob
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task StartJob_MovesAScheduledJobToInProgress()
    {
        var job = await SaveScheduledAsync();

        Clock.Advance(TimeSpan.FromHours(1));
        var result = await Send(new StartJobCommand(OrganizationId, job.Id));

        result.IsSuccess.Should().BeTrue();

        Db.ChangeTracker.Clear();
        var saved = await GetJobAsync(OrganizationId, job.Id);

        saved!.Status.Should().Be(JobStatus.InProgress);
        saved.StartedAtUtc.Should().Be(Clock.GetUtcNow());
        (await ReadJobColumnAsync<string>(job.Id, "status")).Should().Be("InProgress");
    }

    [Fact]
    public async Task StartJob_ForAnUnknownJob_ReturnsNotFound()
    {
        var result = await Send(new StartJobCommand(OrganizationId, Guid.CreateVersion7()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Jobs.Start.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    /// <summary>
    /// Tenant isolation at the request level: another organization's job is indistinguishable
    /// from one that does not exist. With no authentication behind the API, this is the whole
    /// boundary.
    /// </summary>
    [Fact]
    public async Task StartJob_ForAnotherOrganizationsJob_ReturnsNotFound()
    {
        var otherOrganizationId = Guid.CreateVersion7();
        var job = await SaveScheduledAsync(organizationId: otherOrganizationId);

        var result = await Send(new StartJobCommand(OrganizationId, job.Id));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);

        Db.ChangeTracker.Clear();
        (await GetJobAsync(otherOrganizationId, job.Id))!.Status
            .Should().Be(JobStatus.Scheduled, "the other organization's job is untouched");
    }

    [Fact]
    public async Task StartJob_OnADraft_ReturnsAConflict()
    {
        var job = await SaveDraftAsync();

        var result = await Send(new StartJobCommand(OrganizationId, job.Id));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Jobs.Start.InvalidStatus");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task StartJob_OnACompletedJob_ReturnsAConflict()
    {
        var job = await SaveInProgressAsync();
        await Send(new CompleteJobCommand(OrganizationId, job.Id, "https://example.com/s.png"));
        Db.ChangeTracker.Clear();

        var result = await Send(new StartJobCommand(OrganizationId, job.Id));

        result.Error.Code.Should().Be("Jobs.Start.InvalidStatus");
    }

    /* ---------------------------------------------------------------------------------------
     * CompleteJob
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task CompleteJob_CompletesTheJobAndQueuesTheIntegrationEvent()
    {
        var job = await SaveInProgressAsync();

        Clock.Advance(TimeSpan.FromHours(4));
        var result = await Send(new CompleteJobCommand(
            OrganizationId,
            job.Id,
            "https://example.com/signatures/abc.png"));

        result.IsSuccess.Should().BeTrue();

        Db.ChangeTracker.Clear();
        var saved = await GetJobAsync(OrganizationId, job.Id);

        saved!.Status.Should().Be(JobStatus.Completed);
        saved.SignatureUrl.Should().Be("https://example.com/signatures/abc.png");
        saved.CompletedAtUtc.Should().Be(Clock.GetUtcNow());

        (await ReadOutboxAsync(job.Id)).Should().ContainSingle();
    }

    [Fact]
    public async Task CompleteJob_ForAnUnknownJob_ReturnsNotFound()
    {
        var result = await Send(new CompleteJobCommand(
            OrganizationId,
            Guid.CreateVersion7(),
            "https://example.com/s.png"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Jobs.Complete.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    /// <summary>
    /// KNOWN BUG, pinned deliberately. <c>CompleteJobCommandHandler</c> predates the
    /// <c>Error.Conflict</c> factory and uses <c>new Error(...)</c>, which leaves the default
    /// <c>ErrorType.Failure</c>. <c>ApiControllerBase.ToProblem</c> maps that to **500**, where
    /// every sibling handler returns 409 for the same situation.
    ///
    /// When the handler is fixed to use <c>Error.Conflict</c>, this test fails — that is the
    /// signal to flip the expectation to <c>ErrorType.Conflict</c>, not to delete the test.
    /// </summary>
    [Fact]
    public async Task CompleteJob_OnAScheduledJob_ReturnsFailureInsteadOfConflict()
    {
        var job = await SaveScheduledAsync();

        var result = await Send(new CompleteJobCommand(
            OrganizationId,
            job.Id,
            "https://example.com/s.png"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Jobs.Complete.InvalidStatus");
        result.Error.Type.Should().Be(
            ErrorType.Failure,
            "the handler uses new Error(...) rather than Error.Conflict, so this maps to 500");
    }

    [Fact]
    public async Task CompleteJob_WithoutAnAbsoluteSignatureUrl_ReturnsAFailureAndWritesNothing()
    {
        var job = await SaveInProgressAsync();

        var act = async () => await Send(new CompleteJobCommand(OrganizationId, job.Id, "not-a-url"));

        // Job.Complete throws ArgumentException, which the handler does not catch: only
        // DomainException is mapped to a Result. It escapes as an exception instead.
        await act.Should().ThrowAsync<ArgumentException>();

        Db.ChangeTracker.Clear();
        (await GetJobAsync(OrganizationId, job.Id))!.Status.Should().Be(JobStatus.InProgress);
        (await ReadOutboxAsync(job.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteJob_ForAnotherOrganizationsJob_ReturnsNotFound()
    {
        var otherOrganizationId = Guid.CreateVersion7();
        var job = await SaveInProgressAsync(organizationId: otherOrganizationId);

        var result = await Send(new CompleteJobCommand(
            OrganizationId,
            job.Id,
            "https://example.com/s.png"));

        result.Error.Type.Should().Be(ErrorType.NotFound);
        (await ReadOutboxAsync(job.Id)).Should().BeEmpty();
    }

    /* ---------------------------------------------------------------------------------------
     * GetJobById
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task GetJobById_ReturnsTheProjectedResponse()
    {
        var job = await SaveScheduledAsync();

        var result = await Send(new GetJobByIdQuery(OrganizationId, job.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(job.Id);
        result.Value.Status.Should().Be(JobStatus.Scheduled);
        result.Value.OrganizationId.Should().Be(OrganizationId);
        result.Value.Address.Street.Should().Be("123 Main St");
        result.Value.Address.Latitude.Should().Be(30.267200m);
        result.Value.PhotoCount.Should().Be(0);
    }

    [Fact]
    public async Task GetJobById_ForAnUnknownJob_ReturnsNotFound()
    {
        var result = await Send(new GetJobByIdQuery(OrganizationId, Guid.CreateVersion7()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Jobs.GetById.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetJobById_ForAnotherOrganizationsJob_ReturnsNotFound()
    {
        var job = await SaveScheduledAsync(organizationId: Guid.CreateVersion7());

        var result = await Send(new GetJobByIdQuery(OrganizationId, job.Id));

        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    /* ---------------------------------------------------------------------------------------
     * The whole lifecycle
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task Create_Start_Complete_RunsEndToEnd()
    {
        var created = await Send(ACreateCommand(
            scheduledDateUtc: Now.AddDays(1),
            assigneeId: Guid.CreateVersion7()));

        created.IsSuccess.Should().BeTrue();
        var jobId = created.Value;

        Clock.Advance(TimeSpan.FromDays(1));
        (await Send(new StartJobCommand(OrganizationId, jobId))).IsSuccess.Should().BeTrue();

        Clock.Advance(TimeSpan.FromHours(3));
        (await Send(new CompleteJobCommand(
            OrganizationId,
            jobId,
            "https://example.com/signatures/abc.png"))).IsSuccess.Should().BeTrue();

        Db.ChangeTracker.Clear();
        var final = await Send(new GetJobByIdQuery(OrganizationId, jobId));

        final.Value.Status.Should().Be(JobStatus.Completed);
        (await ReadOutboxAsync(jobId)).Should().ContainSingle("only completion is mapped to the outbox");
    }
}
