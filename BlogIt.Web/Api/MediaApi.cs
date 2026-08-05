using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Web.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BlogIt.Web.Api;

public static class MediaApi
{
    public static IEndpointRouteBuilder MapMediaApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/media")
            .WithTags("Media")
            .RequireAuthorization();

        group.MapGet("/", GetMedia);
        group.MapPost("/upload", UploadMedia).DisableAntiforgery();
        group.MapDelete("/{id:guid}", DeleteMedia);

        return app;
    }

    private static async Task<IResult> GetMedia(
        BlogItDbContext db,
        string? q,
        int page = 1,
        int pageSize = 20)
    {
        var query = db.MediaFiles
            .Include(m => m.UploadedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(m => m.Title.Contains(q) || m.FileName.Contains(q));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(m => m.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Results.Ok(new PagedResult<MediaFileDto>(items.Select(ToDto).ToList(), total, page, pageSize));
    }

    private static async Task<IResult> UploadMedia(
        IFormFile file,
        HttpRequest request,
        BlogItDbContext db,
        IBlobService blobService,
        ClaimsPrincipal user)
    {
        var uploaderId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var title = request.Form.TryGetValue("title", out var titleVal) && !string.IsNullOrWhiteSpace(titleVal)
            ? titleVal.ToString()
            : Path.GetFileNameWithoutExtension(file.FileName);

        await using var stream = file.OpenReadStream();
        var (backendUrl, publicPath) = await blobService.UploadAsync(stream, file.FileName, file.ContentType);

        var media = new MediaFile
        {
            Title = title,
            FileName = file.FileName,
            ContentType = file.ContentType,
            BackendUrl = backendUrl,
            PublicPath = publicPath,
            SizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = uploaderId,
        };

        db.MediaFiles.Add(media);
        await db.SaveChangesAsync();
        await db.Entry(media).Reference(m => m.UploadedByUser).LoadAsync();

        return Results.Ok(ToDto(media));
    }

    private static async Task<IResult> DeleteMedia(
        Guid id,
        BlogItDbContext db,
        IBlobService blobService)
    {
        var media = await db.MediaFiles.FindAsync(id);
        if (media is null) return Results.NotFound();

        await blobService.DeleteAsync(media.BackendUrl);
        db.MediaFiles.Remove(media);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static MediaFileDto ToDto(MediaFile m) => new(
        m.Id, m.Title, m.FileName, m.ContentType, m.PublicPath,
        m.SizeBytes, m.UploadedAt,
        m.UploadedByUser?.DisplayName ?? string.Empty
    );
}
