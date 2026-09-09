namespace JobTracker.BackgroundJob.Notifications;

public sealed record NotifyCustomerOfCompletionCommand(Guid JobId, Guid OrganizationId, Guid CustomerId, DateTimeOffset CompletedAtUtc, string IdempotencyKey, string CorrelationId);

public sealed record EmailMessage(string To, string Subject, string PlainTextBody, string HtmlBody);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public interface ICustomerDirectory
{
    Task<CustomerContact?> FindAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken = default);
}

public sealed record CustomerContact(Guid CustomerId, string Email, string DisplayName);

public sealed class CustomerNotFoundException(Guid customerId)
    : Exception($"No contact details are known for customer '{customerId}'.");
