using BlogIt.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Api;

public static class SitemapApi
{
    public static IEndpointRouteBuilder MapSitemapApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sitemap.xml", async (BlogItDbContext db, IConfiguration config, HttpContext http) =>
        {
            var baseUrl = config["SiteUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}";
            baseUrl = baseUrl.TrimEnd('/');

            var posts = await db.BlogPosts
                .Where(p => p.IsPublished && p.PublishedAt != null)
                .Select(p => new { p.Slug, p.PublishedAt, p.CreatedAt, p.UpdatedAt })
                .AsNoTracking()
                .ToListAsync();

            var pages = await db.Pages
                .Where(p => p.IsPublished)
                .Select(p => new { p.Slug, p.UpdatedAt })
                .AsNoTracking()
                .ToListAsync();

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
            sb.AppendLine("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

            void AddUrl(string loc, DateTime? lastMod = null)
            {
                sb.AppendLine("  <url>");
                sb.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(loc)}</loc>");
                if (lastMod.HasValue)
                    sb.AppendLine($"    <lastmod>{lastMod.Value:yyyy-MM-dd}</lastmod>");
                sb.AppendLine("  </url>");
            }

            AddUrl($"{baseUrl}/");
            AddUrl($"{baseUrl}/archive");

            foreach (var post in posts)
                AddUrl(
                    $"{baseUrl}{BlogIt.Shared.Helpers.BlogUrlHelper.GetPostPath(post.Slug, post.PublishedAt, post.CreatedAt)}",
                    post.UpdatedAt);

            foreach (var page in pages)
                AddUrl($"{baseUrl}/{page.Slug}", page.UpdatedAt);

            sb.AppendLine("</urlset>");

            return Results.Content(sb.ToString(), "application/xml");
        }).AllowAnonymous();

        app.MapGet("/robots.txt", (
            IConfiguration config,
            HttpContext http,
            BlogItOptions options) =>
        {
            var baseUrl = config["SiteUrl"] ?? $"{http.Request.Scheme}://{http.Request.Host}";
            baseUrl = baseUrl.TrimEnd('/');

            var content = $"""
                User-agent: *
                Allow: /
                Disallow: {options.AdminPath.TrimEnd('/')}/

                Sitemap: {baseUrl}/sitemap.xml
                """;

            return Results.Content(content, "text/plain");
        }).AllowAnonymous();

        return app;
    }
}
