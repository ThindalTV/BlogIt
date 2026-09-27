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

    /// <summary>
    /// Three outcomes, kept distinguishable because the operator's next move differs for each:
    /// <c>404</c> when no analytics provider is configured, <c>400</c> carrying the provider's own
    /// message when it is configured but unusable, and <c>502</c> when the provider call failed.
    /// </summary>
    /// <remarks>
    /// The shape, the logger category and the split between echoed and generic messages all mirror
    /// <c>AiApi.HandleAiFailure</c>; see the comment there. As in that case the category is the
    /// interface rather than an implementation, because the implementation lives in whichever
    /// satellite package the host installed - this assembly must not name it - and
    /// <c>ILogger&lt;T&gt;</c> cannot name a static class such as this one.
    /// </remarks>
    private static async Task<IResult> GetSummary(
        IAnalyticsService analyticsService,
        ILogger<IAnalyticsService> logger,
        string startDate = "30daysAgo",
        string endDate = "today")
    {
        try
        {
            var summary = await analyticsService.GetSummaryAsync(startDate, endDate);
            return summary is null
                ? Results.NotFound("Analytics is not configured.")
                : Results.Ok(summary);
        }
        catch (InvalidOperationException ex)
        {
            // Reserved, as in AiApi, for known configuration conditions whose text was written to
            // be read by an admin - never a leaked secret or stack trace. Echoed precisely so the
            // dashboard's analytics panel can say what to fix instead of showing the same empty
            // box it shows a site that has not set analytics up at all.
            logger.LogWarning(ex, "Analytics request rejected as misconfigured.");
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Analytics provider request failed.");
            return Results.Problem(
                "The analytics request failed. Please try again.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
