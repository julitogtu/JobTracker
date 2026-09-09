using Microsoft.Extensions.Logging;

namespace JobTracker.BackgroundJob.Billing;

public sealed class InvoiceService(ILogger<InvoiceService> logger) : IInvoiceService
{
    public Task GenerateAsync(GenerateInvoiceCommand command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        logger.LogInformation(
            "Billing: raising invoice for job {JobId} (organization {OrganizationId}, customer {CustomerId}) "
            + "completed at {CompletedAtUtc}. Idempotency key {IdempotencyKey}.",
            command.JobId,
            command.OrganizationId,
            command.CustomerId,
            command.CompletedAtUtc,
            command.IdempotencyKey);

        return Task.CompletedTask;
    }
}
