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

public interface IPublicContentService
{
    Task<IReadOnlyList<BlogPostSummaryDto>> GetRecentPostsAsync(
        int count,
        CancellationToken cancellationToken = default);

    Task<PublicPostPage> GetPostsAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PublicPostPage> SearchPostsAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PublicTagPostPage> GetPostsByTagAsync(
        string tagSlug,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<PublicPostContent?> GetPostAsync(
        string slug,
        bool includeNavigation,
        CancellationToken cancellationToken = default);

    Task<PageDto?> GetPageAsync(
        string slug,
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
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var post = await db.BlogPosts
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
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var page = await db.Pages
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
