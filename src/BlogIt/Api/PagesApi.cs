using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Api;

public static class PagesApi
{
    public static IEndpointRouteBuilder MapPagesApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/pages")
            .WithTags("Pages")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

        group.MapGet("/", GetPages);
        group.MapGet("/{id:guid}", GetPage);
        group.MapPost("/", CreatePage);
        group.MapPut("/{id:guid}", UpdatePage);
        group.MapDelete("/{id:guid}", DeletePage);
        group.MapPut("/{id:guid}/schedule", UpdateSchedule);

        return app;
    }

    private static async Task<IResult> GetPages(
        BlogItDbContext db,
        string? q,
        int page = 1,
        int pageSize = 20)
    {
        (page, pageSize) = Pagination.Clamp(page, pageSize);

        var query = db.Pages.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => p.Title.Contains(q) || p.Slug.Contains(q));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Results.Ok(new PagedResult<PageDto>(items.Select(ToDto).ToList(), total, page, pageSize));
    }

    private static async Task<IResult> GetPage(Guid id, BlogItDbContext db)
    {
        var page = await db.Pages.FindAsync(id);
        return page is null ? Results.NotFound() : Results.Ok(ToDto(page));
    }

    private static async Task<IResult> CreatePage(
        CreatePageRequest req,
        BlogItDbContext db,
        BlogItOptions options)
    {
        var scheduleError = PublicationSchedule.Validate(req.ScheduledPublishAt, req.ScheduledUnpublishAt);
        if (scheduleError is not null)
            return ScheduleValidationProblem(scheduleError);

        if (ValidateFields(
                req.Title, req.Content, req.Slug,
                req.SeoTitle, req.SeoDescription, req.SeoKeywords, req.OgImageUrl)
            is { Count: > 0 } errors)
        {
            return Results.ValidationProblem(errors);
        }

        // Unlike PostsApi, this had no fallback to the title when Slug was left blank — a page saved
        // with a blank Slug field became permanently unreachable, since a page's slug locks on first
        // publish and nothing after creation could change it. The non-empty guard that closed that
        // now lives in SlugHelper, which is also where the title-derived case gained its fallback:
        // rejecting was right for a slug someone typed and a dead end for a site whose titles are
        // simply not Latin.
        if (ResolveNewSlug(req.Slug, req.Title) is not string baseSlug)
            return EmptySlugValidationProblem();
        var slug = await SlugHelper.EnsureUniqueAsync(
            baseSlug, db.Pages.Select(p => p.Slug), ContentLimits.SlugLength);

        var page = new Page
        {
            Title = req.Title,
            Slug = slug,
            Content = req.Content,
            SeoTitle = req.SeoTitle,
            SeoDescription = req.SeoDescription,
            SeoKeywords = req.SeoKeywords,
            OgImageUrl = req.OgImageUrl,
            IsPublished = req.IsPublished,
            HasBeenPublished = req.IsPublished,
            ScheduledPublishAt = req.IsPublished ? null : req.ScheduledPublishAt,
            ScheduledUnpublishAt =
                req.IsPublished || req.ScheduledPublishAt.HasValue ? req.ScheduledUnpublishAt : null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Pages.Add(page);
        // The slug above was checked for availability and is only now being written, so a concurrent
        // create can still take it. See ConcurrencyGuard.
        if (await db.TrySaveAsync(ConcurrencyGuard.RaceLostMessage) is IResult conflict)
            return conflict;
        return Results.Created(
            BlogItPath.Combine(options.ApiPath, $"pages/{page.Id}"),
            ToDto(page));
    }

    private static async Task<IResult> UpdatePage(Guid id, UpdatePageRequest req, BlogItDbContext db)
    {
        var page = await db.Pages.FindAsync(id);
        if (page is null) return Results.NotFound();

        // Before anything is mutated: refuse an edit built on a version of the page that has since
        // been replaced, rather than overwriting the newer one.
        if (ConcurrencyGuard.CheckStamp(page, req.ConcurrencyStamp) is IResult stale)
            return stale;

        var scheduleError = PublicationSchedule.Validate(req.ScheduledPublishAt, req.ScheduledUnpublishAt);
        if (scheduleError is not null)
            return ScheduleValidationProblem(scheduleError);

        // Ahead of the slug block below, which assigns before it is done checking: every rejection
        // from here on leaves the tracked page already partly rewritten, and only the absence of a
        // save keeps that off disk.
        if (ValidateFields(
                req.Title, req.Content, req.Slug,
                req.SeoTitle, req.SeoDescription, req.SeoKeywords, req.OgImageUrl)
            is { Count: > 0 } errors)
        {
            return Results.ValidationProblem(errors);
        }

        var requestedSlug = string.IsNullOrWhiteSpace(req.Slug)
            ? page.Slug
            : SlugHelper.Slugify(req.Slug);
        if (requestedSlug.Length == 0)
            return EmptySlugValidationProblem();
        if (requestedSlug != page.Slug)
        {
            if (page.HasBeenPublished)
                return SlugValidationProblem("A page slug cannot be changed after its first publication.");
            if (await db.Pages.AnyAsync(p => p.Id != id && p.Slug == requestedSlug))
                return SlugValidationProblem("This page slug is already in use.");
            page.Slug = requestedSlug;
        }

        page.Title = req.Title;
        page.Content = req.Content;
        page.SeoTitle = req.SeoTitle;
        page.SeoDescription = req.SeoDescription;
        page.SeoKeywords = req.SeoKeywords;
        page.OgImageUrl = req.OgImageUrl;
        page.IsPublished = req.IsPublished;
        page.HasBeenPublished |= req.IsPublished;
        page.ScheduledPublishAt = req.IsPublished ? null : req.ScheduledPublishAt;
        page.ScheduledUnpublishAt =
            req.IsPublished || req.ScheduledPublishAt.HasValue ? req.ScheduledUnpublishAt : null;
        page.UpdatedAt = DateTime.UtcNow;

        if (await db.TrySaveAsync() is IResult conflict)
            return conflict;
        return Results.Ok(ToDto(page));
    }

    private static async Task<IResult> DeletePage(Guid id, BlogItDbContext db)
    {
        var page = await db.Pages.FindAsync(id);
        if (page is null) return Results.NotFound();

        db.Pages.Remove(page);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateSchedule(
        Guid id,
        UpdatePublicationScheduleRequest req,
        BlogItDbContext db)
    {
        var scheduleError = PublicationSchedule.Validate(req.ScheduledPublishAt, req.ScheduledUnpublishAt);
        if (scheduleError is not null)
            return ScheduleValidationProblem(scheduleError);

        var page = await db.Pages.FindAsync(id);
        if (page is null) return Results.NotFound();

        page.ScheduledPublishAt = page.IsPublished ? null : req.ScheduledPublishAt;
        page.ScheduledUnpublishAt =
            page.IsPublished || req.ScheduledPublishAt.HasValue ? req.ScheduledUnpublishAt : null;
        page.UpdatedAt = DateTime.UtcNow;
        if (await db.TrySaveAsync() is IResult conflict)
            return conflict;
        return Results.Ok(ToDto(page));
    }

    private static PageDto ToDto(Page p) => new(
        p.Id, p.Title, p.Slug, p.Content, p.IsPublished,
        p.CreatedAt, p.UpdatedAt,
        p.SeoTitle, p.SeoDescription, p.SeoKeywords, p.OgImageUrl,
        p.ScheduledPublishAt, p.ScheduledUnpublishAt,
        PublicationSchedule.GetState(p.IsPublished, p.ScheduledPublishAt, p.ScheduledUnpublishAt),
        p.HasBeenPublished,
        p.ConcurrencyStamp
    );

    /// <summary>
    /// Collects every field error on a create or update into one dictionary. The page equivalent of
    /// <c>PostsApi.ValidateFields</c> — Content takes the place of a post's Summary as the required
    /// unbounded column.
    /// </summary>
    private static Dictionary<string, string[]> ValidateFields(
        string? title,
        string? content,
        string? slug,
        string? seoTitle,
        string? seoDescription,
        string? seoKeywords,
        string? ogImageUrl)
    {
        var errors = SeoMetadataValidator.Validate(seoTitle, seoDescription, seoKeywords, ogImageUrl);
        TextFieldValidator.CheckRequired(errors, "title", "Title", title, ContentLimits.TitleLength);
        TextFieldValidator.CheckPresent(errors, "content", "Content", content);
        // Length only: a blank or absent slug is the documented way of asking for one to be derived
        // from the title.
        TextFieldValidator.CheckLength(errors, "slug", "Slug", slug, ContentLimits.SlugLength);
        return errors;
    }

    /// <summary>
    /// The base slug for a new page. The page equivalent of <c>PostsApi.ResolveNewSlug</c>; null when
    /// the caller supplied one that holds nothing a slug can be built from.
    /// </summary>
    private static string? ResolveNewSlug(string? requestedSlug, string title) =>
        string.IsNullOrWhiteSpace(requestedSlug)
            ? SlugHelper.SlugifyOrFallback(title)
            : SlugHelper.Slugify(requestedSlug) is { Length: > 0 } explicitSlug ? explicitSlug : null;

    private static IResult ScheduleValidationProblem(string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["schedule"] = [error] });

    private static IResult SlugValidationProblem(string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["slug"] = [error] });

    private static IResult EmptySlugValidationProblem() =>
        SlugValidationProblem("Slug must contain at least one letter or number.");
}
