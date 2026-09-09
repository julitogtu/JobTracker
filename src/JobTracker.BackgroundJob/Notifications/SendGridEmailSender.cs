using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;

namespace JobTracker.BackgroundJob.Notifications;

public sealed class SendGridEmailSender(ISendGridClient client, IOptionsMonitor<NotificationOptions> options, ILogger<SendGridEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var current = options.CurrentValue;

        var mail = MailHelper.CreateSingleEmail(
            new EmailAddress(current.SenderEmail, current.SenderName),
            new EmailAddress(message.To),
            message.Subject,
            message.PlainTextBody,
            message.HtmlBody);

        var response = await client.SendEmailAsync(mail, cancellationToken);
        var status = (int)response.StatusCode;

        if (status is >= 200 and < 300)
        {
            logger.LogInformation("SendGrid accepted the mail to {Recipient} ({Status}).", message.To, status);
            return;
        }

        var body = await response.Body.ReadAsStringAsync(cancellationToken);

        if (status is >= 400 and < 500 and not 408 and not 429)
        {
            logger.LogError(
                "SendGrid rejected the mail to {Recipient} with {Status}: {Body}. Not retrying.",
                message.To,
                status,
                body);

            return;
        }

        throw new InvalidOperationException(
            $"SendGrid returned {status} for the mail to {message.To}: {body}");
    }
}
