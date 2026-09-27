using BlogIt.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace BlogIt;

internal sealed class EngineEndpointContributor(BlogItOptions options)
    : IBlogItEndpointContributor
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup(options.ApiPath);
        api.MapSetupApi();
        api.MapPreviewApi();
        api.MapAuthApi();
        api.MapPostsApi();
        api.MapPagesApi();
        api.MapMediaApi();
        api.MapUsersApi();
        api.MapSettingsApi();
        api.MapAiApi();
        api.MapAnalyticsApi();
        api.MapRedirectsApi();

        // Tags every route in the group as BlogIt's, so startup validation can tell a collision with
        // the host's own routing from BlogIt colliding with itself. No option relocates an
        // individual API route, so there is no per-route hint to give.
        api.WithMetadata(new BlogItEndpointMetadata(DisableHint: null));

        endpoints.MapMediaProxyApi(options.MediaPath);

        // Root-level documents: each is opt-out, so these two decide per route whether to map
        // anything at all rather than always claiming the URL. Each carries the name of the option
        // that gives the URL back, so a collision can be reported with its own fix.
        endpoints.MapSitemapApi(options);
        endpoints.MapFeedsApi(options);
    }
}
