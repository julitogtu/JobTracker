using FluentAssertions;
using JobTracker.Domain.Common;
using JobTracker.Domain.Enums;
using JobTracker.Domain.Jobs;
using Xunit;

namespace JobTracker.Tests.Domain;

/// <summary>
/// Covers <see cref="Job.AddPhoto"/> and the <see cref="JobPhoto"/> invariants behind it.
/// <c>JobPhoto</c>'s constructor is <c>internal</c> to the domain assembly, so every case here
/// goes through the aggregate — which is the only supported way to create one.
/// </summary>
public class JobPhotoTests
{
    [Fact]
    public void AddPhoto_WhenInProgress_AddsThePhoto()
    {
        var job = JobFactory.InProgress();
        var photoId = Guid.CreateVersion7();
        var capturedAt = JobFactory.StartedAt.AddMinutes(30);

        var photo = job.AddPhoto(photoId, JobFactory.PhotoUrl, capturedAt, "Compressor plate");

        photo.Id.Should().Be(photoId);
        photo.JobId.Should().Be(job.Id);
        photo.Url.Should().Be(JobFactory.PhotoUrl);
        photo.CapturedAtUtc.Should().Be(capturedAt);
        photo.Caption.Should().Be("Compressor plate");
        job.Photos.Should().ContainSingle().Which.Should().Be(photo);
        job.UpdatedAtUtc.Should().Be(capturedAt);
    }

    [Fact]
    public void AddPhoto_NormalizesCapturedAtToUtc()
    {
        var job = JobFactory.InProgress();

        // Same instant as StartedAt, expressed in a +05:00 zone.
        var localCapture = new DateTimeOffset(2026, 1, 6, 14, 5, 0, TimeSpan.FromHours(5));

        var photo = job.AddPhoto(Guid.CreateVersion7(), JobFactory.PhotoUrl, localCapture, null);

        photo.CapturedAtUtc.Offset.Should().Be(TimeSpan.Zero);
        photo.CapturedAtUtc.Should().Be(JobFactory.StartedAt);
        job.UpdatedAtUtc.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void AddPhoto_AcceptsSeveralPhotos()
    {
        var job = JobFactory.InProgress();

        job.AddPhoto(Guid.CreateVersion7(), JobFactory.PhotoUrl, JobFactory.StartedAt, null);
        job.AddPhoto(Guid.CreateVersion7(), "https://example.com/photos/def.jpg", JobFactory.StartedAt, null);

        job.Photos.Should().HaveCount(2);
    }

    [Fact]
    public void AddPhoto_WithDuplicateId_Throws()
    {
        var job = JobFactory.InProgress();
        var photoId = Guid.CreateVersion7();
        job.AddPhoto(photoId, JobFactory.PhotoUrl, JobFactory.StartedAt, null);

        var act = () => job.AddPhoto(photoId, "https://example.com/photos/def.jpg", JobFactory.StartedAt, null);

        act.Should().Throw<DomainException>()
            .WithMessage("*already belongs to this job*");
        job.Photos.Should().ContainSingle();
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.Scheduled)]
    public void AddPhoto_WhenNotInProgress_Throws(JobStatus status)
    {
        var job = status == JobStatus.Draft ? JobFactory.Draft() : JobFactory.Scheduled();

        var act = () => job.AddPhoto(Guid.CreateVersion7(), JobFactory.PhotoUrl, JobFactory.StartedAt, null);

        act.Should().Throw<DomainException>()
            .WithMessage("*only be added while a job is InProgress*");
        job.Photos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/photos/abc.jpg")]
    [InlineData("photos/abc.jpg")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    public void AddPhoto_WithoutAnAbsoluteHttpUrl_Throws(string url)
    {
        var job = JobFactory.InProgress();

        var act = () => job.AddPhoto(Guid.CreateVersion7(), url, JobFactory.StartedAt, null);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("url");
        job.Photos.Should().BeEmpty();
    }

    [Fact]
    public void AddPhoto_WithEmptyId_Throws()
    {
        var job = JobFactory.InProgress();

        var act = () => job.AddPhoto(Guid.Empty, JobFactory.PhotoUrl, JobFactory.StartedAt, null);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("id");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddPhoto_WithBlankCaption_StoresNull(string? caption)
    {
        var job = JobFactory.InProgress();

        var photo = job.AddPhoto(Guid.CreateVersion7(), JobFactory.PhotoUrl, JobFactory.StartedAt, caption);

        photo.Caption.Should().BeNull();
    }

    [Fact]
    public void AddPhoto_TrimsTheCaption()
    {
        var job = JobFactory.InProgress();

        var photo = job.AddPhoto(
            Guid.CreateVersion7(),
            JobFactory.PhotoUrl,
            JobFactory.StartedAt,
            "  Compressor plate  ");

        photo.Caption.Should().Be("Compressor plate");
    }

    [Fact]
    public void AddPhoto_WithCaptionOverMaxLength_Throws()
    {
        var job = JobFactory.InProgress();

        var act = () => job.AddPhoto(
            Guid.CreateVersion7(),
            JobFactory.PhotoUrl,
            JobFactory.StartedAt,
            new string('a', 501));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot exceed 500 characters*");
    }

    /// <summary>The collection is exposed as a read-only projection, not the backing list.</summary>
    [Fact]
    public void Photos_IsNotTheMutableBackingCollection()
    {
        var job = JobFactory.InProgress();

        job.Photos.Should().NotBeAssignableTo<List<JobPhoto>>();
    }
}
