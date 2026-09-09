using Hangfire.Dashboard;

namespace JobTracker.BackgroundJob.Dashboard;

public sealed class DevelopmentDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
