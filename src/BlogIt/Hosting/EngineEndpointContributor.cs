using BlogIt.Api;
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

        endpoints.MapMediaProxyApi(options.MediaPath);
        // Root-level documents: each is opt-out, so these two decide per route whether to map
        // anything at all rather than always claiming the URL.
        endpoints.MapSitemapApi(options);
        endpoints.MapFeedsApi(options);
    }
}
