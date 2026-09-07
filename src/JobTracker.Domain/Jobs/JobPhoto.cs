using JobTracker.Domain.Common;

namespace JobTracker.Domain.Jobs;

public sealed class JobPhoto : Entity
{
    private JobPhoto() {}

    internal JobPhoto(Guid id, Guid jobId, string url, DateTimeOffset capturedAtUtc, string? caption)
        : base(id)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Job ID cannot be empty.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Photo URL is required.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            throw new ArgumentException("Photo URL must be an absolute URI.", nameof(url));
        }

        JobId = jobId;
        Url = url.Trim();
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        Caption = NormalizeCaption(caption);
    }

    public Guid JobId { get; private init; }

    public string Url { get; private init; } = string.Empty;

    public DateTimeOffset CapturedAtUtc { get; private init; }

    public string? Caption { get; private init; }

    private static string? NormalizeCaption(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return null;
        }

        var normalized = caption.Trim();

        if (normalized.Length > 500)
        {
            throw new ArgumentException("Caption cannot exceed 500 characters.", nameof(caption));
        }

        return normalized;
    }
}
