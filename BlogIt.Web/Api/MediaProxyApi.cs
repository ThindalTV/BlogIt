using BlogIt.Shared.Data;
using BlogIt.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Web.Api;

public static class MediaProxyApi
{
    public static IEndpointRouteBuilder MapMediaProxyApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/media/{**path}", ServeMedia)
            .WithTags("Media")
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> ServeMedia(
        string path,
        HttpContext httpContext,
        BlogItDbContext db,
        IBlobService blobService)
    {
        var fileName = Path.GetFileName(path.TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(fileName))
            return Results.NotFound();

        var stream = await blobService.DownloadAsync(fileName);
        if (stream is null)
            return Results.NotFound();

        var mediaFile = await db.MediaFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.FileName == fileName);

        var contentType = mediaFile?.ContentType ?? "application/octet-stream";

        httpContext.Response.Headers.CacheControl = "public, max-age=31536000";

        return Results.Stream(
            stream,
            contentType: contentType,
            fileDownloadName: null,
            lastModified: null,
            entityTag: null,
            enableRangeProcessing: true
        );
    }
}
