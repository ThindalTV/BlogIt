using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Services;

public record PublicPostPage(
    IReadOnlyList<BlogPostSummaryDto> Posts,
    int Page,
    int TotalPages);

public record PublicTagPostPage(
    string? TagName,
    IReadOnlyList<BlogPostSummaryDto> Posts,
    int Page,
    int TotalPages);

public record PublicPostContent(
    BlogPostDetailDto Post,
    BlogPostSummaryDto? PreviousPost,
    BlogPostSummaryDto? NextPost);

/// <summary>
/// Read-only content queries for rendering the public site from a host application.
/// </summary>
/// <remarks>
/// Every method on this interface is published-only unless you explicitly opt out. "Published"
/// means <c>IsPublished</c> is set <em>and</em> <c>PublishedAt</c> has a value, which is also what
/// excludes a post that is scheduled but not yet live. The only opt-out is
/// <c>includeUnpublished</c> on <see cref="GetPostAsync"/> and <see cref="GetPageAsync"/>; pass it
/// only from a path that has already authorized a draft preview through
/// <c>IPreviewTokenService</c>.
/// </remarks>
public interface IPublicContentService
{
    /// <summary>The most recent published posts, newest first.</summary>
    Task<IReadOnlyList<BlogPostSummaryDto>> GetRecentPostsAsync(
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>One page of published posts, newest first. <paramref name="page"/> is 1-based and
    /// clamped into range.</summary>
    Task<PublicPostPage> GetPostsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Published posts whose title, summary or body contains
    /// <paramref name="query"/>. Returns an empty page for a blank query.</summary>
    Task<PublicPostPage> SearchPostsAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>Published posts carrying the given tag, newest first. <c>TagName</c> is null when
    /// no such tag exists.</summary>
    Task<PublicTagPostPage> GetPostsByTagAsync(
        string tagSlug,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>One post by slug, with the adjacent published posts when
    /// <paramref name="includeNavigation"/> is set.</summary>
    /// <param name="includeUnpublished">
    /// Leave at the default (<c>false</c>) for anything a visitor can reach: a draft, or a post
    /// scheduled for later, then returns <c>null</c> rather than the post. Pass <c>true</c> only
    /// after authorizing a preview token — a host that renders whatever this returns will
    /// otherwise publish unpublished work the moment someone guesses a slug.
    /// </param>
    /// <returns>Null when no post matches, or when it matches but is not published and
    /// <paramref name="includeUnpublished"/> is false.</returns>
    Task<PublicPostContent?> GetPostAsync(
        string slug,
        bool includeNavigation,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default);

    /// <summary>One custom page by slug.</summary>
    /// <param name="includeUnpublished">
    /// Same contract as <see cref="GetPostAsync"/>: default <c>false</c> hides unpublished pages,
    /// and <c>true</c> belongs only on an already-authorized preview path.
    /// </param>
    /// <returns>Null when no page matches, or when it matches but is not published and
    /// <paramref name="includeUnpublished"/> is false.</returns>
    Task<PageDto?> GetPageAsync(
        string slug,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default);
}

public sealed class PublicContentService(IDbContextFactory<BlogItDbContext> dbContextFactory)
    : IPublicContentService
{
    public async Task<IReadOnlyList<BlogPostSummaryDto>> GetRecentPostsAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var posts = await PublishedPosts(db)
            .OrderByDescending(post => post.PublishedAt)
            .Take(Math.Max(0, count))
            .Include(post => post.Tags)
            .Include(post => post.Author)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return posts.Select(ToSummaryDto).ToList();
    }

    public async Task<PublicPostPage> GetPostsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = PublishedPosts(db);
        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);

        var posts = await query
            .OrderByDescending(post => post.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(post => post.Tags)
            .Include(post => post.Author)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return new PublicPostPage(posts.Select(ToSummaryDto).ToList(), page, totalPages);
    }

    public async Task<PublicPostPage> SearchPostsAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);

        var searchTerm = query.Trim();
        if (searchTerm.Length == 0)
            return new PublicPostPage([], 1, 1);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query2 = PublishedPosts(db)
            .Where(post =>
                post.Title.Contains(searchTerm)
                || post.Summary.Contains(searchTerm)
                || (post.Content != null && post.Content.Contains(searchTerm)));

        var total = await query2.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);

        // Projects explicitly (rather than loading full BlogPost entities) so the potentially
        // large Content column never comes back over the wire — search results only ever show
        // the summary, matching every other public listing's DTO shape.
        var rows = await query2
            .OrderByDescending(post => post.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(post => new
            {
                post.Id,
                post.Title,
                post.Slug,
                post.Summary,
                HasContent = post.Content != null,
                post.IsPublished,
                post.PublishedAt,
                post.CreatedAt,
                post.UpdatedAt,
                AuthorName = post.Author != null ? post.Author.DisplayName : string.Empty,
                Tags = post.Tags.Select(tag => new TagDto(tag.Id, tag.Name, tag.Slug)).ToList(),
                post.ScheduledPublishAt,
                post.ScheduledUnpublishAt,
                post.HasBeenPublished
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var posts = rows.Select(post => new BlogPostSummaryDto(
            post.Id,
            post.Title,
            post.Slug,
            post.Summary,
            post.HasContent,
            post.IsPublished,
            post.PublishedAt,
            post.CreatedAt,
            post.UpdatedAt,
            post.AuthorName,
            post.Tags,
            post.ScheduledPublishAt,
            post.ScheduledUnpublishAt,
            PublicationSchedule.GetState(post.IsPublished, post.ScheduledPublishAt, post.ScheduledUnpublishAt),
            post.HasBeenPublished)).ToList();

        return new PublicPostPage(posts, page, totalPages);
    }

    public async Task<PublicTagPostPage> GetPostsByTagAsync(
        string tagSlug,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tagName = await db.Tags
            .Where(tag => tag.Slug == tagSlug)
            .Select(tag => tag.Name)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (tagName is null)
            return new PublicTagPostPage(null, [], 1, 1);

        var query = PublishedPosts(db)
            .Where(post => post.Tags.Any(tag => tag.Slug == tagSlug));
        var total = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Min(page, totalPages);

        var posts = await query
            .OrderByDescending(post => post.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(post => post.Tags)
            .Include(post => post.Author)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return new PublicTagPostPage(
            tagName,
            posts.Select(ToSummaryDto).ToList(),
            page,
            totalPages);
    }

    public async Task<PublicPostContent?> GetPostAsync(
        string slug,
        bool includeNavigation,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var post = await (includeUnpublished ? db.BlogPosts : PublishedPosts(db))
            .Where(item => item.Slug == slug)
            .Include(item => item.Tags)
            .Include(item => item.Author)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
        if (post is null)
            return null;

        BlogPost? previous = null;
        BlogPost? next = null;
        if (includeNavigation && post.PublishedAt.HasValue)
        {
            previous = await PublishedPosts(db)
                .Where(item => item.PublishedAt < post.PublishedAt)
                .OrderByDescending(item => item.PublishedAt)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
            next = await PublishedPosts(db)
                .Where(item => item.PublishedAt > post.PublishedAt)
                .OrderBy(item => item.PublishedAt)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new PublicPostContent(
            ToDetailDto(post),
            previous is null ? null : ToSummaryDto(previous),
            next is null ? null : ToSummaryDto(next));
    }

    public async Task<PageDto?> GetPageAsync(
        string slug,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var page = await (includeUnpublished ? db.Pages : db.Pages.Where(item => item.IsPublished))
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Slug == slug, cancellationToken);
        return page is null ? null : ToPageDto(page);
    }

    private static IQueryable<BlogPost> PublishedPosts(BlogItDbContext db) =>
        db.BlogPosts.Where(post => post.IsPublished && post.PublishedAt != null);

    private static BlogPostSummaryDto ToSummaryDto(BlogPost post) => new(
        post.Id,
        post.Title,
        post.Slug,
        post.Summary,
        post.Content is not null,
        post.IsPublished,
        post.PublishedAt,
        post.CreatedAt,
        post.UpdatedAt,
        post.Author?.DisplayName ?? string.Empty,
        post.Tags.Select(tag => new TagDto(tag.Id, tag.Name, tag.Slug)).ToList(),
        post.ScheduledPublishAt,
        post.ScheduledUnpublishAt,
        PublicationSchedule.GetState(
            post.IsPublished,
            post.ScheduledPublishAt,
            post.ScheduledUnpublishAt),
        post.HasBeenPublished);

    private static BlogPostDetailDto ToDetailDto(BlogPost post) => new(
        post.Id,
        post.Title,
        post.Slug,
        post.Summary,
        post.Content,
        post.Content is not null,
        post.IsPublished,
        post.PublishedAt,
        post.CreatedAt,
        post.UpdatedAt,
        post.AuthorId,
        post.Author?.DisplayName ?? string.Empty,
        post.SeoTitle,
        post.SeoDescription,
        post.SeoKeywords,
        post.OgImageUrl,
        post.Tags.Select(tag => new TagDto(tag.Id, tag.Name, tag.Slug)).ToList(),
        post.ScheduledPublishAt,
        post.ScheduledUnpublishAt,
        PublicationSchedule.GetState(
            post.IsPublished,
            post.ScheduledPublishAt,
            post.ScheduledUnpublishAt),
        post.HasBeenPublished);

    private static PageDto ToPageDto(Page page) => new(
        page.Id,
        page.Title,
        page.Slug,
        page.Content,
        page.IsPublished,
        page.CreatedAt,
        page.UpdatedAt,
        page.SeoTitle,
        page.SeoDescription,
        page.SeoKeywords,
        page.OgImageUrl,
        page.ScheduledPublishAt,
        page.ScheduledUnpublishAt,
        PublicationSchedule.GetState(
            page.IsPublished,
            page.ScheduledPublishAt,
            page.ScheduledUnpublishAt),
        page.HasBeenPublished);
}
