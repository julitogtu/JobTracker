using Asp.Versioning;
using JobTracker.Api.Filters;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddPersistence(builder.Configuration.GetConnectionString("DefaultConnection")!);
builder.Services.AddHttpContextAccessor();

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


//builder.Services.AddCors(options => {
//    options.AddPolicy("AdminSite", policyBuilder => {
//        //policyBuilder.WithOrigins(builder.Configuration.GetSection("Cors").Get<string[]>() ?? []);
//        policyBuilder.AllowAnyOrigin();
//        policyBuilder.AllowAnyHeader();
//        policyBuilder.AllowAnyMethod();
//        policyBuilder.AllowCredentials();
//    });
//});

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User.Identity?.Name ?? httpContext.Request.Headers.Host.ToString(),
            factory: partition => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
});

//builder.Services.AddMvc();
var app = builder.Build();

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
