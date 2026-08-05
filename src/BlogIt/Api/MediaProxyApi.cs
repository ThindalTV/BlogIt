using BlogIt.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Api;

public static class MediaProxyApi
{
    public static IEndpointRouteBuilder MapMediaProxyApi(
        this IEndpointRouteBuilder app,
        string mediaPath = BlogItDefaults.MediaPath)
    {
        app.MapGet(BlogItPath.Combine(mediaPath, "{**path}"), ServeMedia)
            .WithTags("Media")
            .AllowAnonymous();

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

        httpContext.Response.Headers.CacheControl = "public, max-age=31536000";

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
