using BlogIt.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogIt;

internal sealed class EngineMiddlewareContributor : IBlogItMiddlewareContributor
{
    public void Configure(IApplicationBuilder application)
    {
        if (application.Properties.ContainsKey("__AuthenticationMiddlewareSet")
            || application.Properties.ContainsKey("__AuthorizationMiddlewareSet"))
        {
            throw new InvalidOperationException(
                "Authentication or authorization middleware was added before UseBlogIt. " +
                "UseBlogIt owns those middleware registrations; remove the duplicate host calls.");
        }

        application.UseUrlRedirects();
        application.UseRateLimiter();
        application.UseAuthentication();
        application.UseAuthorization();
    }
}
