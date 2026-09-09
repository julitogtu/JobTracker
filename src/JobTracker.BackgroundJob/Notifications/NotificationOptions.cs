using System.ComponentModel.DataAnnotations;

namespace JobTracker.BackgroundJob.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    [Required]
    public string SenderEmail { get; set; } = "no-reply@jobtracker.local";

    public string SenderName { get; set; } = "JobTracker";

    public string? SendGridApiKey { get; set; }

    public Dictionary<string, string> CustomerEmails { get; set; } = [];

    public string? FallbackEmail { get; set; }
}
