using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Helpers;
using Microsoft.AspNetCore.RateLimiting;
using System.Text;

namespace BlogIt.Api;

public static class SitemapApi
{
    /// <summary>
    /// Maps <c>/sitemap.xml</c> and <c>/robots.txt</c>, each only when the corresponding
    /// <see cref="BlogItOptions"/> switch is on. A disabled document is not mapped at all rather
    /// than mapped-and-404: a registered route would still collide with the host's own endpoint on
    /// the same path, which is the whole reason the switch exists.
    /// </summary>
    public static IEndpointRouteBuilder MapSitemapApi(
        this IEndpointRouteBuilder app,
        BlogItOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ServeSitemap)
        {
            app.MapGet("/sitemap.xml", GetSitemapAsync)
                .AllowAnonymous()
                .WithName(BlogItEndpointNames.Sitemap)
                // The most expensive anonymous route in the engine: it loads every published post
                // and page, with no cap, where the feeds stop at FeedService.MaxItems. See
                // BlogItRateLimiterPolicies.RootDocument for why this is throttled rather than
                // capped or cached.
                .RequireRateLimiting(BlogItDefaults.RootDocumentRateLimiterPolicy);
        }

        if (options.ServeRobotsTxt)
        {
            app.MapGet("/robots.txt", GetRobotsAsync)
                .AllowAnonymous()
                .WithName(BlogItEndpointNames.RobotsTxt)
                .RequireRateLimiting(BlogItDefaults.RootDocumentRateLimiterPolicy);
        }

        return app;
    }

    public static async Task<IResult> GetSitemapAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration config,
        HttpContext http)
    {
        var siteUrl = SiteUrlResolver.Resolve(
            await settings.GetAsync(SettingKeys.SiteUrl),
            config[SettingKeys.SiteUrl],
            http.Request);

        var entries = await SiteMetadataService.LoadSitemapEntriesAsync(
            db, siteUrl, http.RequestAborted);

        return Results.Content(RenderSitemap(entries), "application/xml");
    }

    public static async Task<IResult> GetRobotsAsync(
        ISettingsService settings,
        IConfiguration config,
        HttpContext http,
        BlogItOptions options)
    {
        var siteUrl = SiteUrlResolver.Resolve(
            await settings.GetAsync(SettingKeys.SiteUrl),
            config[SettingKeys.SiteUrl],
            http.Request);

        return Results.Content(
            RenderRobots(SiteMetadataService.BuildRobotsDirectives(siteUrl, options)),
            "text/plain");
    }

    /// <summary>
    /// Renders sitemap entries as a sitemaps.org 0.9 <c>urlset</c> document. Public so a host that
    /// turned <c>/sitemap.xml</c> off can serve the identical document from its own route.
    /// </summary>
    public static string RenderSitemap(IReadOnlyList<SitemapEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

        foreach (var entry in entries)
        {
            builder.AppendLine("  <url>");
            builder.AppendLine(
                $"    <loc>{System.Security.SecurityElement.Escape(entry.Location)}</loc>");
            if (entry.LastModified.HasValue)
                builder.AppendLine($"    <lastmod>{entry.LastModified.Value:yyyy-MM-dd}</lastmod>");
            builder.AppendLine("  </url>");
        }

        builder.AppendLine("</urlset>");
        return builder.ToString();
    }

    /// <summary>
    /// Renders robots directives as a <c>robots.txt</c> body. Public for the same reason as
    /// <see cref="RenderSitemap"/>; a host merging BlogIt's groups into a larger document will
    /// usually want to walk <see cref="RobotsDirectives"/> itself instead.
    /// </summary>
    public static string RenderRobots(RobotsDirectives directives)
    {
        ArgumentNullException.ThrowIfNull(directives);

        var builder = new StringBuilder();

        foreach (var group in directives.Groups)
        {
            builder.AppendLine($"User-agent: {group.UserAgent}");
            foreach (var allow in group.Allow)
                builder.AppendLine($"Allow: {allow}");
            foreach (var disallow in group.Disallow)
                builder.AppendLine($"Disallow: {disallow}");
            builder.AppendLine();
        }

        foreach (var sitemap in directives.SitemapUrls)
            builder.AppendLine($"Sitemap: {sitemap}");

        // Trailing blank lines are legal but noisy, and the group separator above always leaves
        // one behind when there is no Sitemap: line to follow it.
        return builder.ToString().TrimEnd();
    }
}
