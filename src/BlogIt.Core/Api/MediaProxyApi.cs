using BlogIt.Data;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogIt.Api;

public static class MediaProxyApi
{
    public static IEndpointRouteBuilder MapMediaProxyApi(
        this IEndpointRouteBuilder app,
        string mediaPath = BlogItDefaults.MediaPath)
    {
        app.MapGet(BlogItPath.Combine(mediaPath, "{**path}"), ServeMedia)
            .WithTags("Media")
            .AllowAnonymous()
            // Anonymous, and every hit costs a database lookup plus a storage read. The limit is
            // sized for real visitor traffic — many requests per page view — rather than for admin
            // traffic; BlogItRateLimiterPolicies.Media carries the arithmetic.
            .RequireRateLimiting(BlogItDefaults.MediaRateLimiterPolicy)
            // No option relocates this route on its own; MediaPath moves it.
            .WithMetadata(new BlogItEndpointMetadata(DisableHint: null));

        return app;
    }

    private static async Task<IResult> ServeMedia(
        string path,
        HttpContext httpContext,
        BlogItDbContext db,
        IBlogItMediaStorage mediaStorage)
    {
        var storageKey = path;
        if (string.IsNullOrWhiteSpace(storageKey))
            return Results.NotFound();

        var mediaFile = await db.MediaFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(
                media => media.BackendUrl == storageKey,
                httpContext.RequestAborted);
        if (mediaFile is null)
            return Results.NotFound();

        var stream = await mediaStorage.OpenReadAsync(
            storageKey,
            httpContext.RequestAborted);
        if (stream is null)
            return Results.NotFound();

        // A range request that does not start at byte 0 is a continuation of a view already counted
        // (video seeking, resumed downloads), and HEAD transfers nothing, so neither adds to the
        // count. One atomic UPDATE rather than a tracked read-modify-write, so concurrent hits
        // cannot lose increments.
        var range = httpContext.Request.Headers.Range.ToString();
        var isContinuation = range.Length > 0 && !range.StartsWith("bytes=0-", StringComparison.Ordinal);
        if (!HttpMethods.IsHead(httpContext.Request.Method) && !isContinuation)
        {
            try
            {
                await db.MediaFiles
                    .Where(media => media.Id == mediaFile.Id)
                    .ExecuteUpdateAsync(
                        s => s.SetProperty(media => media.ViewCount, media => media.ViewCount + 1),
                        httpContext.RequestAborted);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best effort: a statistic must never be the reason a file fails to serve, and a
                // provider without set-based updates (EF's in-memory one) cannot count at all.
                if (httpContext.RequestServices.GetService<ILoggerFactory>() is { } loggers)
                    loggers.CreateLogger("BlogIt.Media").LogWarning(ex, "Could not record a media view.");
            }
        }

        httpContext.Response.Headers.CacheControl = "public, max-age=31536000";
        // Defense-in-depth for the accepted Content-Type-trust decision on upload (see
        // MediaApi.cs): stops a browser from re-sniffing a mislabeled file's actual bytes into
        // something more dangerous than its declared type.
        httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";

        return Results.Stream(
            stream,
            contentType: mediaFile.ContentType,
            fileDownloadName: null,
            lastModified: null,
            entityTag: null,
            enableRangeProcessing: true
        );
    }
}
