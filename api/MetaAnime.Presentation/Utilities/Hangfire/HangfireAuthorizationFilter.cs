using Hangfire.Dashboard;

namespace MetaAnime.Utilities.Hangfire;

/// <summary>
/// Authorization filter for Hangfire Dashboard
/// Requires authenticated users to access the dashboard
/// </summary>
public sealed class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Require authenticated user
        // In production, you may want to add role-based authorization
        return httpContext.User.Identity?.IsAuthenticated ?? false;
    }
}
