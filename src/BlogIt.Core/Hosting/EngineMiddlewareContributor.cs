using BlogIt.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogIt;

internal sealed class EngineMiddlewareContributor : IBlogItMiddlewareContributor
{
    public void Configure(IApplicationBuilder application)
    {
        // Previously this threw when the pipeline was already marked with
        // __AuthenticationMiddlewareSet / __AuthorizationMiddlewareSet, telling the host to remove
        // its "duplicate" calls. Those marks are set by *any* UseAuthentication/UseAuthorization
        // call, so the app most likely to embed a blog — one that already authenticates its own
        // users — could not start at all, and the message pointed at code that was not duplicated.
        // BlogIt now yields instead: the host's middleware stays, and BlogIt adds nothing on top of
        // it. That is safe because BlogIt's authorization policy names its own authentication
        // scheme explicitly, so the authorization middleware authenticates BlogIt's bearer tokens
        // for BlogIt endpoints regardless of which UseAuthentication call put it there or what the
        // host's default scheme is.
        var pipeline = BlogItPipelineOptions.From(application);

        application.UseUrlRedirects();

        if (pipeline.AddRateLimiterMiddleware)
        {
            // Unconditional when asked for, and safe to double up: BlogIt's policies count one
            // permit per request even when two rate limiter middlewares are in the pipeline (see
            // BlogItRateLimiterPolicies). There is no pipeline mark to detect a host's own
            // UseRateLimiter, so the flag is the only way to opt out of the second middleware.
            application.UseRateLimiter();
        }

        if (pipeline.ShouldAddAuthentication(application))
        {
            application.UseAuthentication();
        }

        if (pipeline.ShouldAddAuthorization(application))
        {
            application.UseAuthorization();
        }
    }
}
