using BlogIt.Shared.DTOs;
using BlogIt.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogIt.Api;

public static class SetupApi
{
    public static IEndpointRouteBuilder MapSetupApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/setup")
            .WithTags("Setup")
            .AllowAnonymous()
            // Both routes are anonymous and both hit the database. /status is also the liveness probe
            // the admin clients use, so the limit is generous rather than tight — see
            // BlogItRateLimiterPolicies.Setup.
            .RequireRateLimiting(BlogItDefaults.SetupRateLimiterPolicy);

        group.MapGet("/status", async (ISetupService setup) =>
            Results.Ok(new SetupStatusResponse(await setup.IsCompleteAsync())));

        // Deliberately thin. Everything this route used to do inline now lives in ISetupService, so
        // that the programmatic InitializeBlogItAsync entry point cannot validate differently, skip
        // the setup lock, or write settings in a different order. There is one implementation and
        // this is a translation of its result into status codes.
        group.MapPost("/initialize", async (
            SetupInitializeRequest request,
            ISetupService setup) =>
        {
            var result = await setup.InitializeAsync(request);
            return result.Outcome switch
            {
                SetupOutcome.AlreadyComplete =>
                    Results.Conflict("Setup has already been completed."),
                SetupOutcome.ValidationFailed =>
                    Results.ValidationProblem(result.Errors!),
                _ => Results.Ok(new { message = "Setup complete." })
            };
        });

        return app;
    }
}
