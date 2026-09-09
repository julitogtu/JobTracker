namespace JobTracker.BackgroundJob.Billing;

public sealed record GenerateInvoiceCommand(Guid JobId, Guid OrganizationId, Guid CustomerId, DateTimeOffset CompletedAtUtc, string IdempotencyKey, string CorrelationId);

public interface IInvoiceService
{
    Task GenerateAsync(GenerateInvoiceCommand command, CancellationToken cancellationToken = default);
}
