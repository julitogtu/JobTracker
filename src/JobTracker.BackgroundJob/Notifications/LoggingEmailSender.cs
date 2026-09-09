namespace JobTracker.BackgroundJob.Notifications;

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "No SendGrid API key configured, so this mail was not sent. To: {Recipient}. Subject: {Subject}. Body: {Body}",
            message.To,
            message.Subject,
            message.PlainTextBody);

        return Task.CompletedTask;
    }
}
