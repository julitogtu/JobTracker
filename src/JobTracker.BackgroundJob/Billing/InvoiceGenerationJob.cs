using Hangfire;
using JobTracker.BackgroundJob.Correlation;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace JobTracker.BackgroundJob.Billing;

[AutomaticRetry(Attempts = 10, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public sealed class InvoiceGenerationJob(IInvoiceService invoiceService, ILogger<InvoiceGenerationJob> logger)
{
    public const string Queue = "billing";

    public async Task ExecuteAsync(GenerateInvoiceCommand command, CancellationToken cancellationToken)
    {
        using var correlation = AmbientCorrelationIdAccessor.Push(command.CorrelationId);
        using var logProperty = LogContext.PushProperty("CorrelationId", command.CorrelationId);

        logger.LogInformation(
            "Generating invoice for job {JobId}.",
            command.JobId);

        await invoiceService.GenerateAsync(command, cancellationToken);
    }
}
