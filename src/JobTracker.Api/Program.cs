using Asp.Versioning;
using JobTracker.Api.Filters;
using JobTracker.Api.Middleware;
using JobTracker.Application.Common.Correlation;
using Scalar.AspNetCore;
using Serilog;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "JobTracker.Api"));

var connectionString =
    builder.Configuration.GetConnectionString("JobsDatabase")
    ?? throw new InvalidOperationException(
        "Connection string JobsDatabase was not configured.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICorrelationIdAccessor, HttpCorrelationIdAccessor>();

builder.Services.AddApplication();
builder.Services.AddPersistence(connectionString);

builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ApiVersionReader = new HeaderApiVersionReader("x-api-version");
}).AddApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
}).AddOpenApi();

builder.Services.AddControllersWithViews(options =>
            options.Filters.Add<ApiExceptionFilterAttribute>());


builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminSite", policyBuilder =>
    {
        // TODO: AllowAnyOrigin for local testing support. In production, this should be restricted to the admin site domain.
        //policyBuilder.WithOrigins(builder.Configuration.GetSection("Cors").Get<string[]>() ?? []);
        policyBuilder.AllowAnyOrigin();
        policyBuilder.AllowAnyHeader();
        policyBuilder.AllowAnyMethod();
        policyBuilder.AllowCredentials();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 2,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithDocumentPerVersion();

    app.MapScalarApiReference(options =>
    {
        var descriptions = app.DescribeApiVersions();

        for (var i = 0; i < descriptions.Count; i++)
        {
            var description = descriptions[i];
            var isDefault = i == descriptions.Count - 1;

            options.AddDocument(description.GroupName, description.GroupName, isDefault: isDefault);
        }
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
