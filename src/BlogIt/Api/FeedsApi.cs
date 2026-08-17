using BlogIt.Shared.Data;
using BlogIt.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace BlogIt.Api;

public static class FeedsApi
{
    public const string RssContentType = "application/rss+xml; charset=utf-8";
    public const string AtomContentType = "application/atom+xml; charset=utf-8";

    /// <summary>
    /// Maps <c>/rss.xml</c> and <c>/atom.xml</c>, each only when the corresponding
    /// <see cref="BlogItOptions"/> switch is on. See <see cref="SitemapApi.MapSitemapApi"/> for why
    /// a disabled feed is left unmapped rather than mapped to a 404.
    /// </summary>
    public static IEndpointRouteBuilder MapFeedsApi(
        this IEndpointRouteBuilder app,
        BlogItOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ServeRssFeed)
        {
            app.MapGet("/rss.xml", GetRssAsync)
                .AllowAnonymous()
                .WithName(BlogItEndpointNames.RssFeed)
                .Produces(StatusCodes.Status200OK, contentType: RssContentType)
                // Same policy as /sitemap.xml. These are cheaper — capped at FeedService.MaxItems —
                // but they are the same kind of route: anonymous, crawler-facing, and backed by a
                // query. Sharing one bucket keeps the root documents consistent.
                .RequireRateLimiting(BlogItDefaults.RootDocumentRateLimiterPolicy);
        }

        if (options.ServeAtomFeed)
        {
            app.MapGet("/atom.xml", GetAtomAsync)
                .AllowAnonymous()
                .WithName(BlogItEndpointNames.AtomFeed)
                .Produces(StatusCodes.Status200OK, contentType: AtomContentType)
                .RequireRateLimiting(BlogItDefaults.RootDocumentRateLimiterPolicy);
        }

        return app;
    }

    public static async Task<IResult> GetRssAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var xml = await FeedService.CreateRssAsync(
            db, settings, configuration, httpContext.Request, cancellationToken);
        return Results.Content(xml, RssContentType, Encoding.UTF8);
    }

    public static async Task<IResult> GetAtomAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration configuration,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var xml = await FeedService.CreateAtomAsync(
            db, settings, configuration, httpContext.Request, cancellationToken);
        return Results.Content(xml, AtomContentType, Encoding.UTF8);
    }
}
