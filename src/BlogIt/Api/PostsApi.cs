using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BlogIt.Api;

public static class PostsApi
{
    public static IEndpointRouteBuilder MapPostsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/posts")
            .WithTags("Posts")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

        group.MapGet("/", GetPosts);
        group.MapGet("/{id:guid}", GetPost);
        group.MapPost("/", CreatePost);
        group.MapPut("/{id:guid}", UpdatePost);
        group.MapDelete("/{id:guid}", DeletePost);
        group.MapPost("/{id:guid}/publish", PublishPost);
        group.MapPost("/{id:guid}/unpublish", UnpublishPost);
        group.MapPut("/{id:guid}/schedule", UpdateSchedule);

        return app;
    }

    private static async Task<IResult> GetPosts(
        BlogItDbContext db,
        string? q,
        int page = 1,
        int pageSize = 20,
        string status = "all")
    {
        var query = db.BlogPosts
            .Include(p => p.Tags)
            .Include(p => p.Author)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(p => p.Title.Contains(q) || p.Summary.Contains(q));

        query = status switch
        {
            "published" => query.Where(p => p.IsPublished),
            "draft" => query.Where(p => !p.IsPublished),
            "scheduled" => query.Where(p => p.ScheduledPublishAt != null || p.ScheduledUnpublishAt != null),
            _ => query
        };

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(ToSummaryDto).ToList();
        return Results.Ok(new PagedResult<BlogPostSummaryDto>(dtos, total, page, pageSize));
    }

    private static async Task<IResult> GetPost(Guid id, BlogItDbContext db)
    {
        var post = await db.BlogPosts
            .Include(p => p.Tags)
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == id);

        return post is null ? Results.NotFound() : Results.Ok(ToDetailDto(post));
    }

    private static async Task<IResult> CreatePost(
        CreateBlogPostRequest req,
        BlogItDbContext db,
        BlogItOptions options,
        ClaimsPrincipal user)
    {
        var scheduleError = PublicationSchedule.Validate(req.ScheduledPublishAt, req.ScheduledUnpublishAt);
        if (scheduleError is not null)
            return ScheduleValidationProblem(scheduleError);

        var authorId = Guid.Parse(user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var baseSlug = SlugHelper.Slugify(
            string.IsNullOrWhiteSpace(req.Slug) ? req.Title : req.Slug);
        var existingSlugs = await db.BlogPosts.Select(p => p.Slug).ToListAsync();
        var slug = SlugHelper.EnsureUnique(baseSlug, existingSlugs);

        var post = new BlogPost
        {
            Title = req.Title,
            Slug = slug,
            Summary = req.Summary,
            Content = req.Content,
            SeoTitle = req.SeoTitle,
            SeoDescription = req.SeoDescription,
            SeoKeywords = req.SeoKeywords,
            OgImageUrl = req.OgImageUrl,
            ScheduledPublishAt = req.ScheduledPublishAt,
            ScheduledUnpublishAt = req.ScheduledPublishAt.HasValue ? req.ScheduledUnpublishAt : null,
            AuthorId = authorId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        post.Tags = await TagResolver.ResolveAsync(db, req.TagNames);
        db.BlogPosts.Add(post);
        await db.SaveChangesAsync();

        await db.Entry(post).Reference(p => p.Author).LoadAsync();

        return Results.Created(
            BlogItPath.Combine(options.ApiPath, $"posts/{post.Id}"),
            ToDetailDto(post));
    }

    private static async Task<IResult> UpdatePost(
        Guid id,
        UpdateBlogPostRequest req,
        BlogItDbContext db)
    {
        var post = await db.BlogPosts
            .Include(p => p.Tags)
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (post is null) return Results.NotFound();

        var requestedSlug = string.IsNullOrWhiteSpace(req.Slug)
            ? post.Slug
            : SlugHelper.Slugify(req.Slug);
        if (requestedSlug != post.Slug)
        {
            if (post.PublishedAt.HasValue)
                return SlugValidationProblem("A post slug cannot be changed after its first publication.");
            if (await db.BlogPosts.AnyAsync(p => p.Id != id && p.Slug == requestedSlug))
                return SlugValidationProblem("This post slug is already in use.");
            post.Slug = requestedSlug;
        }

        var scheduleError = PublicationSchedule.Validate(req.ScheduledPublishAt, req.ScheduledUnpublishAt);
        if (scheduleError is not null)
            return ScheduleValidationProblem(scheduleError);

        post.Title = req.Title;
        post.Summary = req.Summary;
        post.Content = req.Content;
        post.SeoTitle = req.SeoTitle;
        post.SeoDescription = req.SeoDescription;
        post.SeoKeywords = req.SeoKeywords;
        post.OgImageUrl = req.OgImageUrl;
        post.ScheduledPublishAt = post.IsPublished ? null : req.ScheduledPublishAt;
        post.ScheduledUnpublishAt =
            post.IsPublished || req.ScheduledPublishAt.HasValue ? req.ScheduledUnpublishAt : null;
        post.UpdatedAt = DateTime.UtcNow;
        post.Tags = await TagResolver.ResolveAsync(db, req.TagNames);

        await db.SaveChangesAsync();
        return Results.Ok(ToDetailDto(post));
    }

    private static async Task<IResult> DeletePost(Guid id, BlogItDbContext db)
    {
        var post = await db.BlogPosts.FindAsync(id);
        if (post is null) return Results.NotFound();

        db.BlogPosts.Remove(post);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> PublishPost(Guid id, BlogItDbContext db)
    {
        var post = await db.BlogPosts
            .Include(p => p.Tags)
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (post is null) return Results.NotFound();

        if (!post.IsPublished)
        {
            post.IsPublished = true;
            post.PublishedAt ??= DateTime.UtcNow;
        }
        post.HasBeenPublished = true;
        post.ScheduledPublishAt = null;
        post.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(ToDetailDto(post));
    }

    private static async Task<IResult> UnpublishPost(Guid id, BlogItDbContext db)
    {
        var post = await db.BlogPosts
            .Include(p => p.Tags)
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (post is null) return Results.NotFound();

        post.IsPublished = false;
        post.ScheduledPublishAt = null;
        post.ScheduledUnpublishAt = null;
        post.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(ToDetailDto(post));
    }

    private static async Task<IResult> UpdateSchedule(
        Guid id,
        UpdatePublicationScheduleRequest req,
        BlogItDbContext db)
    {
        var scheduleError = PublicationSchedule.Validate(req.ScheduledPublishAt, req.ScheduledUnpublishAt);
        if (scheduleError is not null)
            return ScheduleValidationProblem(scheduleError);

        var post = await db.BlogPosts
            .Include(p => p.Tags)
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (post is null) return Results.NotFound();

        post.ScheduledPublishAt = post.IsPublished ? null : req.ScheduledPublishAt;
        post.ScheduledUnpublishAt =
            post.IsPublished || req.ScheduledPublishAt.HasValue ? req.ScheduledUnpublishAt : null;
        post.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Results.Ok(ToDetailDto(post));
    }

    private static BlogPostSummaryDto ToSummaryDto(BlogPost p) => new(
        p.Id, p.Title, p.Slug, p.Summary,
        p.Content is not null,
        p.IsPublished, p.PublishedAt, p.CreatedAt, p.UpdatedAt,
        p.Author?.DisplayName ?? string.Empty,
        p.Tags.Select(t => new TagDto(t.Id, t.Name, t.Slug)).ToList(),
        p.ScheduledPublishAt, p.ScheduledUnpublishAt,
        PublicationSchedule.GetState(p.IsPublished, p.ScheduledPublishAt, p.ScheduledUnpublishAt),
        p.HasBeenPublished
    );

    private static BlogPostDetailDto ToDetailDto(BlogPost p) => new(
        p.Id, p.Title, p.Slug, p.Summary, p.Content,
        p.Content is not null,
        p.IsPublished, p.PublishedAt, p.CreatedAt, p.UpdatedAt,
        p.AuthorId, p.Author?.DisplayName ?? string.Empty,
        p.SeoTitle, p.SeoDescription, p.SeoKeywords, p.OgImageUrl,
        p.Tags.Select(t => new TagDto(t.Id, t.Name, t.Slug)).ToList(),
        p.ScheduledPublishAt, p.ScheduledUnpublishAt,
        PublicationSchedule.GetState(p.IsPublished, p.ScheduledPublishAt, p.ScheduledUnpublishAt),
        p.HasBeenPublished
    );

    private static IResult ScheduleValidationProblem(string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["schedule"] = [error] });

    private static IResult SlugValidationProblem(string error) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["slug"] = [error] });
}
