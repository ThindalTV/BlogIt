using BlogIt.Contracts;
using BlogIt.Contracts.DTOs;
using BlogIt.Data;
using BlogIt.Entities;
using BlogIt.Helpers;
using BlogIt.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace BlogIt.Api;

public static class MediaApi
{
    public static IEndpointRouteBuilder MapMediaApi(this IEndpointRouteBuilder app)
    {
        // Resolved rather than taken as a parameter so the public signature stays as it was.
        var options = app.ServiceProvider.GetRequiredService<BlogItOptions>();

        var group = app.MapGroup("/media")
            .WithTags("Media")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

        group.MapGet("/", GetMedia);
        group.MapPost("/upload", UploadMedia)
            .DisableAntiforgery()
            // The upload endpoint carries its own body-size limit rather than inheriting the
            // server's. Routing applies this to the request after matching and before the body is
            // read, so it governs in both directions: a host with a small global limit does not
            // silently cap the blog, and a host with a large one does not let BlogIt's own ceiling
            // be bypassed.
            .WithMetadata(new BlogItRequestSizeLimit(options.MaxMediaUploadBytes))
            // The handler reads the form itself, so the IFormFile parameter that would normally
            // describe this endpoint is gone. Declared explicitly to keep the OpenAPI shape.
            .Accepts<IFormFile>("multipart/form-data");
        group.MapPut("/{id:guid}", UpdateMedia);
        group.MapPut("/{id:guid}/content", ReplaceMedia)
            .DisableAntiforgery()
            .WithMetadata(new BlogItRequestSizeLimit(options.MaxMediaUploadBytes))
            .Accepts<IFormFile>("multipart/form-data");
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
        {
            var pattern = SearchPattern.Containing(q);
            query = query.Where(m =>
                EF.Functions.Like(m.Title, pattern, SearchPattern.EscapeCharacter)
                || EF.Functions.Like(m.FileName, pattern, SearchPattern.EscapeCharacter));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(m => m.UploadedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Results.Ok(new PagedResult<MediaFileDto>(items.Select(ToDto).ToList(), total, page, pageSize));
    }

    /// <remarks>
    /// Takes the raw <see cref="HttpRequest"/> and reads the form itself rather than binding an
    /// <c>IFormFile</c> parameter. Binding reads the body, so an oversize upload used to fail
    /// <em>inside</em> binding — before this method ran — and every carefully worded validation
    /// response below was unreachable. What the portal got was a bare 413 with no body, which it
    /// could only report as an unexplained failure. Reading the form here puts the failure somewhere
    /// a <c>catch</c> can reach it and turn it into an answer that names the limit.
    /// </remarks>
    private static async Task<IResult> UploadMedia(
        HttpRequest request,
        BlogItDbContext db,
        IBlogItMediaStorage mediaStorage,
        BlogItOptions options,
        ClaimsPrincipal user)
    {
        var uploaderId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        }
        catch (BadHttpRequestException ex)
            when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // The server enforced the endpoint's size metadata and stopped reading. Translated here
            // so the response is BlogIt's, not an empty framework 413.
            return TooLarge(options);
        }

        if (form.Files.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["A file is required."]
            });
        }

        var file = form.Files[0];

        // Checked again on the file itself, because the endpoint's size metadata is only as good as
        // the server implementing it — and not every server does. This makes the limit BlogIt's own
        // guarantee rather than a property of the deployment, and it measures the file rather than
        // the request, so multipart framing cannot push a file that is within the limit over it.
        if (file.Length > options.MaxMediaUploadBytes)
            return TooLarge(options);

        var title = form.TryGetValue("title", out var titleVal) && !string.IsNullOrWhiteSpace(titleVal)
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

        // Optional pretty URL. Checked before StoreAsync for the same reason as the fields above.
        // Two uploads racing for the same path are caught by the unique index instead, which takes
        // the compensating-delete path below.
        var slug = MediaSlug.Normalize(form.TryGetValue("slug", out var slugVal) ? slugVal.ToString() : null);
        await CheckSlugAsync(errors, db, slug, excludeId: null, request.HttpContext.RequestAborted);

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
            Slug = slug,
            PublicPath = BlogItPath.MediaPublicPath(options, slug ?? storageKey),
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

    /// <summary>
    /// Edits the title and the pretty URL. Setting a new slug moves the file's public URL; the
    /// storage-key URL keeps working, but a previous slug stops resolving, so anything that linked
    /// to it needs updating.
    /// </summary>
    private static async Task<IResult> UpdateMedia(
        Guid id,
        UpdateMediaRequest req,
        BlogItDbContext db,
        BlogItOptions options,
        HttpContext httpContext)
    {
        var media = await db.MediaFiles.Include(m => m.UploadedByUser).FirstOrDefaultAsync(m => m.Id == id);
        if (media is null) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        TextFieldValidator.CheckRequired(errors, "title", "Title", req.Title, ContentLimits.TitleLength);
        var slug = MediaSlug.Normalize(req.Slug);
        await CheckSlugAsync(errors, db, slug, excludeId: id, httpContext.RequestAborted);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        media.Title = req.Title;
        media.Slug = slug;
        media.PublicPath = BlogItPath.MediaPublicPath(options, slug ?? media.BackendUrl);
        await db.SaveChangesAsync();

        return Results.Ok(ToDto(media));
    }

    /// <summary>
    /// Replaces a file's content in place: same storage key, same URLs, new bytes.
    /// </summary>
    /// <remarks>
    /// The stored object is written first and the row updated second. The two cannot be committed
    /// together; if the row save fails the new content is already live under the old size and
    /// content type, and repeating the request repairs that. The other order would leave a row
    /// advertising content that was never written.
    /// </remarks>
    private static async Task<IResult> ReplaceMedia(
        Guid id,
        HttpRequest request,
        BlogItDbContext db,
        IBlogItMediaStorage mediaStorage,
        BlogItOptions options)
    {
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        }
        catch (BadHttpRequestException ex)
            when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge(options);
        }

        if (form.Files.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = ["A file is required."]
            });
        }

        var file = form.Files[0];
        if (file.Length > options.MaxMediaUploadBytes)
            return TooLarge(options);

        var errors = new Dictionary<string, string[]>();
        TextFieldValidator.CheckLength(
            errors, "fileName", "File name", file.FileName, ContentLimits.FileNameLength);
        TextFieldValidator.CheckLength(
            errors, "contentType", "Content type", file.ContentType, ContentLimits.ContentTypeLength);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var media = await db.MediaFiles
            .Include(m => m.UploadedByUser)
            .FirstOrDefaultAsync(m => m.Id == id, request.HttpContext.RequestAborted);
        if (media is null) return Results.NotFound();

        // The same trust decision as on upload: the client-supplied Content-Type is stored as-is.
        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        await using var stream = file.OpenReadStream();
        try
        {
            await mediaStorage.ReplaceAsync(
                media.BackendUrl, stream, contentType, request.HttpContext.RequestAborted);
        }
        catch (NotSupportedException ex)
        {
            return Results.Problem(
                title: "Replacing media is not supported by the configured storage.",
                detail: ex.Message,
                statusCode: StatusCodes.Status501NotImplemented);
        }

        media.FileName = file.FileName;
        media.ContentType = contentType;
        media.SizeBytes = file.Length;
        media.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Results.Ok(ToDto(media));
    }

    /// <summary>
    /// A slug must be well-formed and must not collide with another file's slug or storage key —
    /// the proxy resolves both, so either would make two files answer to one URL.
    /// </summary>
    private static async Task CheckSlugAsync(
        Dictionary<string, string[]> errors,
        BlogItDbContext db,
        string? slug,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        if (slug is null)
            return;

        var problem = MediaSlug.Validate(slug);
        if (problem is null
            && await db.MediaFiles.AnyAsync(
                m => m.Id != excludeId && (m.Slug == slug || m.BackendUrl == slug),
                cancellationToken))
        {
            problem = "Another file already uses this path.";
        }

        if (problem is not null)
            errors["slug"] = [problem];
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

    /// <summary>
    /// The response for an upload that exceeds <see cref="BlogItOptions.MaxMediaUploadBytes"/>.
    /// </summary>
    /// <remarks>
    /// Carries <c>maxBytes</c> as well as prose so the admin portal can say what the ceiling is
    /// rather than only that one was hit.
    /// </remarks>
    private static IResult TooLarge(BlogItOptions options) => Results.Json(
        new
        {
            title = "Upload too large.",
            status = StatusCodes.Status413PayloadTooLarge,
            detail =
                $"The file exceeds the {Megabytes(options.MaxMediaUploadBytes)} MB upload limit. "
                + "Raise BlogItOptions.MaxMediaUploadBytes, and raise any reverse-proxy or IIS "
                + "body-size limit to match.",
            maxBytes = options.MaxMediaUploadBytes
        },
        statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>Whole megabytes, for a limit stated in a message a person reads.</summary>
    private static long Megabytes(long bytes) => bytes / (1024 * 1024);

    private static MediaFileDto ToDto(MediaFile m) => new(
        m.Id, m.Title, m.FileName, m.ContentType, m.PublicPath,
        m.SizeBytes, UtcTimestamp.ToOffset(m.UploadedAt),
        m.UploadedByUser?.DisplayName ?? string.Empty,
        m.ViewCount,
        m.Slug
    );
}
