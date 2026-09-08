using FluentAssertions;
using JobTracker.Domain.Enums;
using JobTracker.Domain.Jobs;
using Xunit;

namespace JobTracker.Tests.Integration;

/// <summary>
/// The search path is the one place where the SQL is genuinely non-trivial: a stored
/// <c>tsvector</c> computed column with a GIN index for full text, and PostgreSQL row-value
/// comparison for keyset paging. None of it survives translation to a fake provider, so all of
/// it is exercised against the real database.
/// </summary>
public sealed class JobSearchTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /* ---------------------------------------------------------------------------------------
     * Filters
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task Search_ReturnsOnlyTheRequestedOrganization()
    {
        var mine = await SaveScheduledAsync();
        await SaveScheduledAsync(organizationId: Guid.CreateVersion7());

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact]
    public async Task Search_FiltersByStatus()
    {
        var draft = await SaveDraftAsync();
        var scheduled = await SaveScheduledAsync();
        var inProgress = await SaveInProgressAsync();

        var page = await SearchAsync(new JobSearchCriteria(
            OrganizationId,
            Statuses: [JobStatus.Scheduled, JobStatus.InProgress]));

        page.Items.Select(item => item.Id).Should().BeEquivalentTo([scheduled.Id, inProgress.Id]);
        page.Items.Should().NotContain(item => item.Id == draft.Id);
    }

    [Fact]
    public async Task Search_WithNoStatuses_DoesNotFilterByStatus()
    {
        await SaveDraftAsync();
        await SaveScheduledAsync();

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, Statuses: []));

        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_FiltersByAssignee()
    {
        var assigneeId = Guid.CreateVersion7();
        var mine = await SaveScheduledAsync(assigneeId: assigneeId);
        await SaveScheduledAsync();

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, AssigneeId: assigneeId));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(mine.Id);
    }

    [Fact]
    public async Task Search_FiltersByScheduledDateRange()
    {
        var early = await SaveScheduledAsync(scheduledFor: Now.AddDays(1));
        var middle = await SaveScheduledAsync(scheduledFor: Now.AddDays(5));
        var late = await SaveScheduledAsync(scheduledFor: Now.AddDays(10));

        var page = await SearchAsync(new JobSearchCriteria(
            OrganizationId,
            ScheduledFromUtc: Now.AddDays(3),
            ScheduledToUtc: Now.AddDays(7)));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(middle.Id);
        page.Items.Should().NotContain(item => item.Id == early.Id || item.Id == late.Id);
    }

    /// <summary>The range bounds are inclusive (<c>&gt;=</c> and <c>&lt;=</c>).</summary>
    [Fact]
    public async Task Search_DateRangeBoundsAreInclusive()
    {
        var job = await SaveScheduledAsync(scheduledFor: Now.AddDays(5));

        var page = await SearchAsync(new JobSearchCriteria(
            OrganizationId,
            ScheduledFromUtc: Now.AddDays(5),
            ScheduledToUtc: Now.AddDays(5)));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(job.Id);
    }

    /// <summary>Unscheduled jobs have a null date, so a date filter excludes them entirely.</summary>
    [Fact]
    public async Task Search_DateFilter_ExcludesUnscheduledJobs()
    {
        await SaveDraftAsync();

        var page = await SearchAsync(new JobSearchCriteria(
            OrganizationId,
            ScheduledFromUtc: Now));

        page.Items.Should().BeEmpty();
    }

    /* ---------------------------------------------------------------------------------------
     * Full-text search
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task Search_MatchesOnTitle()
    {
        var match = await SaveDraftAsync(title: "Replace furnace igniter", description: "Site visit.");
        await SaveDraftAsync(title: "Unclog kitchen sink", description: "Site visit.");

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, SearchTerm: "furnace"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(match.Id);
    }

    [Fact]
    public async Task Search_MatchesOnDescription()
    {
        var match = await SaveDraftAsync(title: "Site visit", description: "Replace the failed compressor.");
        await SaveDraftAsync(title: "Site visit", description: "Unclog the kitchen sink.");

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, SearchTerm: "compressor"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(match.Id);
    }

    /// <summary>
    /// The column is <c>to_tsvector('english', ...)</c>, so matching is stemmed rather than
    /// literal — "compressors" finds "compressor".
    /// </summary>
    [Fact]
    public async Task Search_IsStemmed()
    {
        var match = await SaveDraftAsync(title: "Fix HVAC", description: "Replace the failed compressor.");

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, SearchTerm: "compressors"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(match.Id);
    }

    [Fact]
    public async Task Search_IsCaseInsensitive()
    {
        var match = await SaveDraftAsync(title: "Fix HVAC", description: "Site visit.");

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, SearchTerm: "hvac"));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(match.Id);
    }

    [Fact]
    public async Task Search_ReturnsNothingForATermThatDoesNotMatch()
    {
        await SaveDraftAsync(title: "Fix HVAC", description: "Replace the failed compressor.");

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, SearchTerm: "plumbing"));

        page.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_WithABlankTerm_DoesNotFilter(string? searchTerm)
    {
        await SaveDraftAsync();
        await SaveDraftAsync();

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, SearchTerm: searchTerm));

        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_CombinesFullTextWithTheOtherFilters()
    {
        var match = await SaveScheduledAsync(title: "Fix HVAC", description: "Replace the compressor.");
        await SaveDraftAsync(title: "Fix HVAC", description: "Replace the compressor.");

        var page = await SearchAsync(new JobSearchCriteria(
            OrganizationId,
            SearchTerm: "compressor",
            Statuses: [JobStatus.Scheduled]));

        page.Items.Should().ContainSingle().Which.Id.Should().Be(match.Id);
    }

    /* ---------------------------------------------------------------------------------------
     * Ordering and keyset paging
     * ------------------------------------------------------------------------------------ */

    /// <summary>
    /// Ordering is <c>ScheduledDateUtc ?? 9999-12-31</c> then <c>Id</c>, so unscheduled jobs sort
    /// last rather than first as a plain nullable sort would put them.
    /// </summary>
    [Fact]
    public async Task Search_OrdersByScheduledDate_WithUnscheduledLast()
    {
        var unscheduled = await SaveDraftAsync();
        var late = await SaveScheduledAsync(scheduledFor: Now.AddDays(10));
        var early = await SaveScheduledAsync(scheduledFor: Now.AddDays(1));

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId));

        page.Items.Select(item => item.Id).Should().ContainInOrder(early.Id, late.Id, unscheduled.Id);
    }

    [Fact]
    public async Task Search_PagesThroughEveryRowExactlyOnce()
    {
        var expected = new List<Guid>();

        for (var day = 1; day <= 5; day++)
        {
            expected.Add((await SaveScheduledAsync(scheduledFor: Now.AddDays(day))).Id);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var page = await SearchAsync(new JobSearchCriteria(OrganizationId, Cursor: cursor, PageSize: 2));

            seen.AddRange(page.Items.Select(item => item.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 10);

        pages.Should().Be(3);
        seen.Should().HaveCount(5);
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().ContainInOrder(expected);
    }

    [Fact]
    public async Task Search_ReturnsACursorOnlyWhenAnotherPageExists()
    {
        await SaveScheduledAsync(scheduledFor: Now.AddDays(1));
        await SaveScheduledAsync(scheduledFor: Now.AddDays(2));

        var full = await SearchAsync(new JobSearchCriteria(OrganizationId, PageSize: 1));
        var last = await SearchAsync(new JobSearchCriteria(OrganizationId, PageSize: 10));

        full.NextCursor.Should().NotBeNull();
        last.NextCursor.Should().BeNull();
    }

    /// <summary>The cursor is base64url: it must survive a query string without escaping.</summary>
    [Fact]
    public async Task Search_CursorIsUrlSafe()
    {
        await SaveScheduledAsync(scheduledFor: Now.AddDays(1));
        await SaveScheduledAsync(scheduledFor: Now.AddDays(2));

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, PageSize: 1));

        page.NextCursor.Should().NotBeNullOrEmpty();
        page.NextCursor.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    /// <summary>
    /// Keyset paging is stable under inserts: a row added before the cursor position does not
    /// shift the next page, which is the whole reason this is not offset paging.
    /// </summary>
    [Fact]
    public async Task Search_IsStableWhenAnEarlierRowIsInsertedMidPage()
    {
        await SaveScheduledAsync(scheduledFor: Now.AddDays(2));
        await SaveScheduledAsync(scheduledFor: Now.AddDays(3));
        var third = await SaveScheduledAsync(scheduledFor: Now.AddDays(4));

        var first = await SearchAsync(new JobSearchCriteria(OrganizationId, PageSize: 2));

        // A new job that sorts before everything already returned.
        await SaveScheduledAsync(scheduledFor: Now.AddDays(1));

        var second = await SearchAsync(new JobSearchCriteria(
            OrganizationId,
            Cursor: first.NextCursor,
            PageSize: 2));

        second.Items.Should().ContainSingle().Which.Id.Should().Be(third.Id);
    }

    [Fact]
    public async Task Search_ClampsPageSizeToAtLeastOne()
    {
        await SaveScheduledAsync(scheduledFor: Now.AddDays(1));
        await SaveScheduledAsync(scheduledFor: Now.AddDays(2));

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, PageSize: 0));

        page.Items.Should().ContainSingle();
        page.NextCursor.Should().NotBeNull();
    }

    [Fact]
    public async Task Search_ClampsPageSizeToOneHundred()
    {
        for (var index = 0; index < 101; index++)
        {
            var job = NewDraft();
            job.Schedule(Now.AddDays(1).AddMinutes(index), Guid.CreateVersion7(), Clock.GetUtcNow());
            await AddJobAsync(job);
        }

        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var page = await SearchAsync(new JobSearchCriteria(OrganizationId, PageSize: 1_000));

        page.Items.Should().HaveCount(100);
        page.NextCursor.Should().NotBeNull();
    }

    [Theory]
    [InlineData("not-base64!!")]
    [InlineData("bm90LWpzb24")]
    public async Task Search_WithAnUnreadableCursor_Throws(string cursor)
    {
        var act = async () => await SearchAsync(new JobSearchCriteria(OrganizationId, Cursor: cursor));

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*pagination cursor is invalid*");
    }

    /* ---------------------------------------------------------------------------------------
     * Projection
     * ------------------------------------------------------------------------------------ */

    [Fact]
    public async Task Search_ProjectsTheFlattenedAddress()
    {
        await SaveScheduledAsync();

        var item = (await SearchAsync(new JobSearchCriteria(OrganizationId))).Items.Single();

        item.Street.Should().Be("123 Main St");
        item.City.Should().Be("Austin");
        item.State.Should().Be("TX");
        item.ZipCode.Should().Be("78701");
        item.Latitude.Should().Be(30.267200m);
        item.Longitude.Should().Be(-97.743100m);
    }

    [Fact]
    public async Task Search_ProjectsThePhotoCountWithoutLoadingPhotos()
    {
        var job = await SaveInProgressAsync();
        var tracked = await GetJobAsync(OrganizationId, job.Id);
        tracked!.AddPhoto(Guid.CreateVersion7(), "https://example.com/photos/a.jpg", Clock.GetUtcNow(), null);
        tracked.AddPhoto(Guid.CreateVersion7(), "https://example.com/photos/b.jpg", Clock.GetUtcNow(), null);
        await SaveChangesAsync();
        Db.ChangeTracker.Clear();

        var item = (await SearchAsync(new JobSearchCriteria(OrganizationId))).Items.Single();

        item.PhotoCount.Should().Be(2);
    }

    [Fact]
    public async Task Search_WithNoMatches_ReturnsAnEmptyPageNotNull()
    {
        var page = await SearchAsync(new JobSearchCriteria(OrganizationId));

        page.Items.Should().BeEmpty();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Search_WithNullCriteria_Throws()
    {
        var act = async () => await Jobs.SearchAsync(null!, Ct);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
