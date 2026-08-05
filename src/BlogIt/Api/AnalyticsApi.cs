using BlogIt.Services;

namespace BlogIt.Api;

public static class AnalyticsApi
{
    public static IEndpointRouteBuilder MapAnalyticsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/analytics")
            .WithTags("Analytics")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

        group.MapGet("/summary", GetSummary);

        return app;
    }

    private static async Task<IResult> GetSummary(
        IAnalyticsService analyticsService,
        string startDate = "30daysAgo",
        string endDate = "today")
    {
        var summary = await analyticsService.GetSummaryAsync(startDate, endDate);
        return summary is null ? Results.NotFound("Analytics is not configured.") : Results.Ok(summary);
    }
}
