using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using BlogIt.Services;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BlogIt.Api;

public static class MediaApi
{
    public static IEndpointRouteBuilder MapMediaApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/media")
            .WithTags("Media")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

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
        (page, pageSize) = Pagination.Clamp(page, pageSize);

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
        IBlogItMediaStorage mediaStorage,
        BlogItOptions options,
        ClaimsPrincipal user)
    {
        var uploaderId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var title = request.Form.TryGetValue("title", out var titleVal) && !string.IsNullOrWhiteSpace(titleVal)
            ? titleVal.ToString()
            : Path.GetFileNameWithoutExtension(file.FileName);

        // Length only, not required: the fallback above already guarantees non-null, and a blank
        // title is what a file named like ".gitignore" legitimately resolves to — the column accepts
        // that, so rejecting it here would break an upload that works today. Checked before
        // StoreAsync so a 400 cannot leave an unreferenced blob in the storage provider, which is
        // what validating after the write would do on every rejection.
        //
        // FileName and ContentType are checked on the same terms and for the same reason as Title,
        // and were missed because neither is part of a request body anyone validates: both are
        // whatever the browser put in the multipart headers, and both land in bounded columns. Length
        // only again, since IFormFile reports an absent value as an empty string, which stores fine.
        var errors = new Dictionary<string, string[]>();
        TextFieldValidator.CheckLength(errors, "title", "Title", title, ContentLimits.TitleLength);
        TextFieldValidator.CheckLength(
            errors, "fileName", "File name", file.FileName, ContentLimits.FileNameLength);
        TextFieldValidator.CheckLength(
            errors, "contentType", "Content type", file.ContentType, ContentLimits.ContentTypeLength);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        // INTENTIONAL: the client-supplied Content-Type is trusted as-is, with no server-side
        // magic-byte validation or allow-list — this endpoint requires authentication, so
        // whatever gets uploaded (including an .html file that executes same-origin script
        // when visited) can only originate from a trusted, already-authenticated user, not an
        // anonymous visitor. Consistent with the "every user is a fully trusted author"
        // decision documented in MarkdownHelper.cs and AUDIT_REPORT.md finding #0.
        await using var stream = file.OpenReadStream();
        var storageKey = await mediaStorage.StoreAsync(
            stream,
            file.FileName,
            file.ContentType,
            request.HttpContext.RequestAborted);

        var media = new MediaFile
        {
            Title = title,
            FileName = file.FileName,
            ContentType = file.ContentType,
            BackendUrl = storageKey,
            PublicPath = BlogItPath.MediaPublicPath(options, storageKey),
            SizeBytes = file.Length,
            UploadedAt = DateTime.UtcNow,
            UploadedByUserId = uploaderId,
        };

        db.MediaFiles.Add(media);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            // The blob and the row cannot be committed together, so the object that was just stored
            // is now unreferenced: nothing knows its key, and no later request can ever produce it.
            // Deleting it here is the only chance to clean it up. Best-effort by design — if this
            // delete also fails the original save exception is what the caller needs to see, and an
            // orphaned blob costs storage but breaks nothing.
            //
            // Not cancellation-aware for the same reason: RequestAborted is very likely already
            // cancelled when the save failed because the client went away, and passing it would skip
            // the cleanup in exactly the case that created the orphan. CancellationToken.None makes
            // the compensation run regardless.
            try
            {
                await mediaStorage.DeleteAsync(storageKey, CancellationToken.None);
            }
            catch
            {
                // Swallowed deliberately: rethrowing here would replace the save failure — the
                // actual cause — with a secondary cleanup failure.
            }

            throw;
        }

        await db.Entry(media).Reference(m => m.UploadedByUser).LoadAsync();

        return Results.Ok(ToDto(media));
    }

    private static async Task<IResult> DeleteMedia(
        Guid id,
        BlogItDbContext db,
        IBlogItMediaStorage mediaStorage,
        HttpContext httpContext)
    {
        var media = await db.MediaFiles.FindAsync(id);
        if (media is null) return Results.NotFound();

        // Row first, object second. These two stores cannot be committed atomically, so the only
        // choice is which way a partial failure lands. Deleting the object first meant a failing
        // SaveChangesAsync left a row pointing at nothing: a permanent 404 for that media with no
        // repair path short of hand-editing the database. This order fails into an orphaned object
        // instead — storage nobody references, which a sweep can reclaim and which breaks nothing in
        // the meantime — and it keeps the delete retryable, since the row is gone before the object.
        db.MediaFiles.Remove(media);
        await db.SaveChangesAsync();
        await mediaStorage.DeleteAsync(media.BackendUrl, httpContext.RequestAborted);
        return Results.NoContent();
    }

    private static MediaFileDto ToDto(MediaFile m) => new(
        m.Id, m.Title, m.FileName, m.ContentType, m.PublicPath,
        m.SizeBytes, m.UploadedAt,
        m.UploadedByUser?.DisplayName ?? string.Empty
    );
}
