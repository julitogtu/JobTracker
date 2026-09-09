using System.Globalization;
using System.Net;
using Hangfire;
using JobTracker.BackgroundJob.Correlation;
using Serilog.Context;

namespace JobTracker.BackgroundJob.Notifications;

[AutomaticRetry(Attempts = 10, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class CustomerNotificationJob(ICustomerDirectory customerDirectory, IEmailSender emailSender, ILogger<CustomerNotificationJob> logger)
{
    public const string Queue = "notifications";

    public async Task ExecuteAsync(NotifyCustomerOfCompletionCommand command, CancellationToken cancellationToken)
    {
        using var correlation = AmbientCorrelationIdAccessor.Push(command.CorrelationId);
        using var logProperty = LogContext.PushProperty("CorrelationId", command.CorrelationId);

        var contact = await customerDirectory.FindAsync(
            command.OrganizationId,
            command.CustomerId,
            cancellationToken) ?? throw new CustomerNotFoundException(command.CustomerId);

        logger.LogInformation("Notifying customer {CustomerId} that job {JobId} is complete.", command.CustomerId, command.JobId);

        await emailSender.SendAsync(Compose(command, contact), cancellationToken);
    }

    private static EmailMessage Compose(NotifyCustomerOfCompletionCommand command, CustomerContact contact)
    {
        var completedAt = command.CompletedAtUtc
            .ToUniversalTime()
            .ToString("f", CultureInfo.InvariantCulture);

        var subject = $"Your job is complete ({command.JobId:D})";

        var plainText =
            $"""
             Hello {contact.DisplayName},

             Your job {command.JobId:D} was completed on {completedAt} UTC.

             Your invoice will follow shortly.

             -- JobTracker
             """;

        var html =
            $"""
             <p>Hello {WebUtility.HtmlEncode(contact.DisplayName)},</p>
             <p>Your job <strong>{command.JobId:D}</strong> was completed on {WebUtility.HtmlEncode(completedAt)} UTC.</p>
             <p>Your invoice will follow shortly.</p>
             <p>&mdash; JobTracker</p>
             """;

        return new EmailMessage(contact.Email, subject, plainText, html);
    }
}
