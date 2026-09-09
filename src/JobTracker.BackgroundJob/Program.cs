using Hangfire;
using Hangfire.PostgreSql;
using JobTracker.Application.Common.Correlation;
using JobTracker.Application.Common.Messaging;
using JobTracker.BackgroundJob.Billing;
using JobTracker.BackgroundJob.Correlation;
using JobTracker.BackgroundJob.Dashboard;
using JobTracker.BackgroundJob.Notifications;
using JobTracker.BackgroundJob.Outbox;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SendGrid;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "JobTracker.BackgroundJob"));

var connectionString =
    builder.Configuration.GetConnectionString("JobsDatabase")
    ?? throw new InvalidOperationException(
        "Connection string JobsDatabase was not configured.");

builder.Services.AddOptions<OutboxDispatchOptions>()
    .Bind(builder.Configuration.GetSection(OutboxDispatchOptions.SectionName));

builder.Services.AddOptions<NotificationOptions>()
    .Bind(builder.Configuration.GetSection(NotificationOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<AmbientCorrelationIdAccessor>();
builder.Services.AddSingleton<ICorrelationIdAccessor>(sp =>
    sp.GetRequiredService<AmbientCorrelationIdAccessor>());

builder.Services.AddPersistence(connectionString);

builder.Services.Replace(
    ServiceDescriptor.Scoped<IOutboxMessagePublisher, HangfireOutboxDispatcher>());

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        postgres => postgres.UseNpgsqlConnection(connectionString),
        new PostgreSqlStorageOptions
        {
            SchemaName = "hangfire",
            PrepareSchemaIfNecessary = true,
            QueuePollInterval = TimeSpan.FromSeconds(5)
        }));

builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["default", InvoiceGenerationJob.Queue, CustomerNotificationJob.Queue];
    options.WorkerCount = Math.Max(4, Environment.ProcessorCount);
});

builder.Services.AddScoped<OutboxDispatchJob>();

builder.Services.AddScoped<InvoiceGenerationJob>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();

builder.Services.AddScoped<CustomerNotificationJob>();
builder.Services.AddScoped<ICustomerDirectory, ConfiguredCustomerDirectory>();
AddEmailSender(builder);

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new DevelopmentDashboardAuthorizationFilter()]
    });
}

app.MapGet("/", () => Results.Ok(new { status = "healthy", service = "JobTracker.BackgroundJob" }));

var dispatchOptions = app.Services.GetRequiredService<IOptions<OutboxDispatchOptions>>().Value;

app.Services.GetRequiredService<IRecurringJobManager>().AddOrUpdate<OutboxDispatchJob>(
    OutboxDispatchJob.RecurringJobId,
    job => job.ExecuteAsync(CancellationToken.None),
    dispatchOptions.CronExpression,
    new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });

app.Run();

static void AddEmailSender(WebApplicationBuilder builder)
{
    var apiKey = builder.Configuration[$"{NotificationOptions.SectionName}:SendGridApiKey"];

    if (string.IsNullOrWhiteSpace(apiKey))
    {
        builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
        return;
    }

    builder.Services.AddHttpClient(nameof(SendGridClient))
        .AddTypedClient<ISendGridClient>((httpClient, serviceProvider) =>
            new SendGridClient(
                httpClient,
                serviceProvider.GetRequiredService<IOptions<NotificationOptions>>().Value.SendGridApiKey));

    builder.Services.AddScoped<IEmailSender, SendGridEmailSender>();
}
