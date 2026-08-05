using BlogIt.Shared.Data;
using BlogIt.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace BlogIt.Api;

public static class FeedsApi
{
    public const string RssContentType = "application/rss+xml; charset=utf-8";
    public const string AtomContentType = "application/atom+xml; charset=utf-8";

    public static IEndpointRouteBuilder MapFeedsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/rss.xml", GetRssAsync)
            .AllowAnonymous()
            .WithName("RssFeed")
            .Produces(StatusCodes.Status200OK, contentType: RssContentType);

        app.MapGet("/atom.xml", GetAtomAsync)
            .AllowAnonymous()
            .WithName("AtomFeed")
            .Produces(StatusCodes.Status200OK, contentType: AtomContentType);

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
