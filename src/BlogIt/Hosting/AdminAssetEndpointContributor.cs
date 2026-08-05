using BlogIt.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace BlogIt;

internal sealed class AdminAssetEndpointContributor(
    BlogItOptions options,
    BlogItAdminAssets assets)
    : IBlogItEndpointContributor
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                BlogItPath.Combine(options.AdminPath, BlogItAdminBootstrapConfig.RelativePath),
                (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    var apiPath = context.Request.PathBase
                        .Add(new PathString(options.ApiPath))
                        .Value ?? options.ApiPath;
                    return Results.Json(new BlogItAdminBootstrapConfig(apiPath));
                })
            .AllowAnonymous();

        endpoints.MapMethods(
                BlogItPath.Combine(options.AdminPath, "{**path:nonfile}"),
                [HttpMethods.Get, HttpMethods.Head],
                (HttpContext context) =>
                    assets.WriteShellAsync(
                        context,
                        options.AdminPath,
                        context.RequestAborted))
            .AllowAnonymous();
    }
}
