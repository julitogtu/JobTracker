using FluentAssertions;
using JobTracker.BackgroundJob.Notifications;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace JobTracker.Tests.BackgroundJob;

public sealed class CustomerNotificationJobTests
{
    private static readonly DateTimeOffset CompletedAt = new(2026, 3, 1, 10, 30, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RecordingEmailSender emails = new();

    [Fact]
    public async Task AKnownCustomer_IsEmailed()
    {
        var command = ACommand();
        var job = JobFor(command.CustomerId, "sam@example.com");

        await job.ExecuteAsync(command, Ct);

        emails.Sent.Should().ContainSingle();
        emails.Sent[0].To.Should().Be("sam@example.com");
        emails.Sent[0].Subject.Should().Contain(command.JobId.ToString("D"));
        emails.Sent[0].PlainTextBody.Should().Contain(command.JobId.ToString("D"));
    }

    [Fact]
    public async Task AnUnknownCustomer_FailsTheJobInsteadOfSendingNothing()
    {
        var command = ACommand();
        var job = JobFor(Guid.CreateVersion7(), "someone-else@example.com");

        var execute = async () => await job.ExecuteAsync(command, Ct);

        await execute.Should().ThrowAsync<CustomerNotFoundException>();
        emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownCustomer_FallsBackWhenAFallbackAddressIsConfigured()
    {
        var command = ACommand();
        var job = JobFor(Guid.CreateVersion7(), "someone-else@example.com", fallback: "dev-inbox@example.com");

        await job.ExecuteAsync(command, Ct);

        emails.Sent.Should().ContainSingle();
        emails.Sent[0].To.Should().Be("dev-inbox@example.com");
    }

    [Fact]
    public async Task TheHtmlBody_EncodesTheDisplayName()
    {
        var command = ACommand();

        var job = new CustomerNotificationJob(
            new StubCustomerDirectory(
                new CustomerContact(command.CustomerId, "sam@example.com", "<script>alert(1)</script>")),
            emails,
            NullLogger<CustomerNotificationJob>.Instance);

        await job.ExecuteAsync(command, Ct);

        emails.Sent[0].HtmlBody.Should().NotContain("<script>");
        emails.Sent[0].HtmlBody.Should().Contain("&lt;script&gt;");
    }

    private CustomerNotificationJob JobFor(Guid knownCustomerId, string email, string? fallback = null)
    {
        var options = new NotificationOptions
        {
            CustomerEmails = { [knownCustomerId.ToString()] = email },
            FallbackEmail = fallback
        };

        return new CustomerNotificationJob(
            new ConfiguredCustomerDirectory(new StaticOptionsMonitor<NotificationOptions>(options)),
            emails,
            NullLogger<CustomerNotificationJob>.Instance);
    }

    private static NotifyCustomerOfCompletionCommand ACommand() =>
        new(
            JobId: Guid.CreateVersion7(),
            OrganizationId: Guid.CreateVersion7(),
            CustomerId: Guid.CreateVersion7(),
            CompletedAtUtc: CompletedAt,
            IdempotencyKey: "key",
            CorrelationId: "corr-1");

    private sealed class StubCustomerDirectory(CustomerContact contact) : ICustomerDirectory
    {
        public Task<CustomerContact?> FindAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CustomerContact?>(contact);
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly List<EmailMessage> sent = [];

        public IReadOnlyList<EmailMessage> Sent => sent;

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            sent.Add(message);
            return Task.CompletedTask;
        }
    }
}

internal sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
