using Microsoft.Extensions.Options;

namespace JobTracker.BackgroundJob.Notifications;

public sealed class ConfiguredCustomerDirectory(IOptionsMonitor<NotificationOptions> options) : ICustomerDirectory
{
    public Task<CustomerContact?> FindAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var current = options.CurrentValue;

        if (current.CustomerEmails.TryGetValue(customerId.ToString(), out var email) && !string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult<CustomerContact?>(new CustomerContact(customerId, email, $"Customer {customerId:D}"));
        }

        if (!string.IsNullOrWhiteSpace(current.FallbackEmail))
        {
            return Task.FromResult<CustomerContact?>(new CustomerContact(customerId, current.FallbackEmail, $"Customer {customerId:D}"));
        }

        return Task.FromResult<CustomerContact?>(null);
    }
}
