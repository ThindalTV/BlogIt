using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Services;

public record PublicPostPage(
    IReadOnlyList<BlogPostSummaryDto> Posts,
    int Page,
    int TotalPages)
{
    /// <summary>
    /// Posts matching the query across every page.
    /// </summary>
    /// <remarks>
    /// The figure behind <see cref="TotalPages"/>, which is this divided by the requested page size.
    /// Exposed because a host cannot recover it from <see cref="TotalPages"/> — the last page's
    /// length is unknown — and so could not render "47 results" without issuing a second count query
    /// of its own.
    /// </remarks>
    public int TotalCount { get; init; }
}

public record PublicTagPostPage(
    string? TagName,
    IReadOnlyList<BlogPostSummaryDto> Posts,
    int Page,
    int TotalPages)
{
    /// <inheritdoc cref="PublicPostPage.TotalCount"/>
    public int TotalCount { get; init; }
}

public record PublicPostContent(
    BlogPostDetailDto Post,
    BlogPostSummaryDto? PreviousPost,
    BlogPostSummaryDto? NextPost);

/// <summary>
/// How many published posts fall in one UTC calendar month, for building an archive index.
/// </summary>
/// <param name="Year">The UTC year.</param>
/// <param name="Month">The UTC month, 1-12.</param>
/// <param name="Count">Published posts published within that month.</param>
public record PostArchiveCount(int Year, int Month, int Count);

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

    /// <summary>
    /// One page of published posts whose publication instant falls in
    /// <c>[fromInclusive, toExclusive)</c>, newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interval is half-open so that consecutive ranges tile without a post published exactly on
    /// a boundary appearing in both. An empty or inverted range returns an empty page rather than
    /// throwing.
    /// </para>
    /// <para>
    /// Takes instants rather than a year and month because BlogIt has no notion of a site timezone,
    /// and a <c>(year, month)</c> signature would silently pick one. A host wanting UTC months
    /// passes <c>new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero)</c> and that value plus
    /// one month; a host wanting local months converts its own boundaries first, which this shape
    /// allows and a month-based one could not.
    /// </para>
    /// </remarks>
    Task<PublicPostPage> GetPostsByDateRangeAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// How many published posts fall in each UTC calendar month that has any, newest month first.
    /// </summary>
    /// <remarks>
    /// For rendering an archive index. Buckets by UTC month, matching
    /// <see cref="GetPostsByDateRangeAsync"/> when it is called with UTC month boundaries; a host
    /// paging by local months should expect a post published within an offset's distance of
    /// midnight on the first or last of a month to be counted in the adjacent bucket. The result
    /// only changes when something is published, so cache it in the host rather than calling it per
    /// request.
    /// </remarks>
    Task<IReadOnlyList<PostArchiveCount>> GetArchiveCountsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>One tag by slug, or null when no such tag exists.</summary>
    /// <remarks>
    /// For the common case of titling a tag page without running a listing query for it. Returns the
    /// tag whether or not it currently has any published posts, so "no such tag" (null) and "a real
    /// tag with nothing published under it" stay distinguishable — which
    /// <see cref="GetPostsByTagAsync"/>'s <c>TagName</c> cannot do, since it overloads null with
    /// both meanings.
    /// </remarks>
    Task<TagDto?> GetTagAsync(
        string slug,
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
        var posts = await db.BlogPosts.WherePublished()
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
        var query = db.BlogPosts.WherePublished();
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
        return new PublicPostPage(posts.Select(ToSummaryDto).ToList(), page, totalPages)
        {
            TotalCount = total
        };
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
            return new PublicPostPage([], 1, 1) { TotalCount = 0 };

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query2 = db.BlogPosts.WherePublished()
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
                post.HasBeenPublished,
                // Comes back even though Content deliberately does not: the word count is the one
                // thing about a body a listing cannot derive once the body itself is left behind.
                post.WordCount
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
            UtcTimestamp.ToOffset(post.PublishedAt),
            UtcTimestamp.ToOffset(post.CreatedAt),
            UtcTimestamp.ToOffset(post.UpdatedAt),
            post.AuthorName,
            post.Tags,
            UtcTimestamp.ToOffset(post.ScheduledPublishAt),
            UtcTimestamp.ToOffset(post.ScheduledUnpublishAt),
            PublicationSchedule.GetState(
                post.IsPublished,
                UtcTimestamp.ToOffset(post.ScheduledPublishAt),
                UtcTimestamp.ToOffset(post.ScheduledUnpublishAt)),
            post.HasBeenPublished)
        {
            WordCount = post.WordCount
        }).ToList();

        return new PublicPostPage(posts, page, totalPages) { TotalCount = total };
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
            return new PublicTagPostPage(null, [], 1, 1) { TotalCount = 0 };

        var query = db.BlogPosts.WherePublished()
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
            totalPages)
        {
            TotalCount = total
        };
    }

    public async Task<PublicPostPage> GetPostsByDateRangeAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Max(1, pageSize);

        // Entities store UTC DateTime, so compare against that rather than the offsets the caller
        // handed us. Whole-value range comparisons are also the only date filtering EF can translate
        // on these columns — see UtcDateTimeConverter — and they use the (IsPublished, PublishedAt)
        // index, which a DATEPART-style predicate would not.
        var from = UtcTimestamp.ToStorage(fromInclusive);
        var to = UtcTimestamp.ToStorage(toExclusive);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.BlogPosts.WherePublished()
            .Where(post => post.PublishedAt >= from && post.PublishedAt < to);

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

        return new PublicPostPage(posts.Select(ToSummaryDto).ToList(), page, totalPages)
        {
            TotalCount = total
        };
    }

    public async Task<IReadOnlyList<PostArchiveCount>> GetArchiveCountsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Grouped in memory rather than in SQL, for three reasons that all point the same way:
        // EF cannot translate .Year/.Month on a value-converted column at all (see
        // UtcDateTimeConverter); GROUP BY DATEPART(...) could not use the index even if it could be
        // translated; and this reads one 8-byte column straight out of that index, so even a large
        // blog is a few hundred kilobytes. Hosts should cache the result — it only moves when
        // something is published.
        var publishedAt = await db.BlogPosts.WherePublished()
            .AsNoTracking()
            .Select(post => post.PublishedAt!.Value)
            .ToListAsync(cancellationToken);

        return publishedAt
            .GroupBy(value => (value.Year, value.Month))
            .Select(group => new PostArchiveCount(group.Key.Year, group.Key.Month, group.Count()))
            .OrderByDescending(entry => entry.Year)
            .ThenByDescending(entry => entry.Month)
            .ToList();
    }

    public async Task<TagDto?> GetTagAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tags
            .Where(tag => tag.Slug == slug)
            .Select(tag => new TagDto(tag.Id, tag.Name, tag.Slug))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PublicPostContent?> GetPostAsync(
        string slug,
        bool includeNavigation,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var post = await (includeUnpublished ? db.BlogPosts : db.BlogPosts.WherePublished())
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
            previous = await db.BlogPosts.WherePublished()
                .Where(item => item.PublishedAt < post.PublishedAt)
                .OrderByDescending(item => item.PublishedAt)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
            next = await db.BlogPosts.WherePublished()
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
        var page = await (includeUnpublished ? db.Pages : db.Pages.WherePublished())
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Slug == slug, cancellationToken);
        return page is null ? null : ToPageDto(page);
    }

    private static BlogPostSummaryDto ToSummaryDto(BlogPost post) => new(
        post.Id,
        post.Title,
        post.Slug,
        post.Summary,
        post.Content is not null,
        post.IsPublished,
        UtcTimestamp.ToOffset(post.PublishedAt),
        UtcTimestamp.ToOffset(post.CreatedAt),
        UtcTimestamp.ToOffset(post.UpdatedAt),
        post.Author?.DisplayName ?? string.Empty,
        post.Tags.Select(tag => new TagDto(tag.Id, tag.Name, tag.Slug)).ToList(),
        UtcTimestamp.ToOffset(post.ScheduledPublishAt),
        UtcTimestamp.ToOffset(post.ScheduledUnpublishAt),
        PublicationSchedule.GetState(
            post.IsPublished,
            UtcTimestamp.ToOffset(post.ScheduledPublishAt),
            UtcTimestamp.ToOffset(post.ScheduledUnpublishAt)),
        post.HasBeenPublished)
    {
        WordCount = post.WordCount
    };

    private static BlogPostDetailDto ToDetailDto(BlogPost post) => new(
        post.Id,
        post.Title,
        post.Slug,
        post.Summary,
        post.Content,
        post.Content is not null,
        post.IsPublished,
        UtcTimestamp.ToOffset(post.PublishedAt),
        UtcTimestamp.ToOffset(post.CreatedAt),
        UtcTimestamp.ToOffset(post.UpdatedAt),
        post.AuthorId,
        post.Author?.DisplayName ?? string.Empty,
        post.SeoTitle,
        post.SeoDescription,
        post.SeoKeywords,
        post.OgImageUrl,
        post.Tags.Select(tag => new TagDto(tag.Id, tag.Name, tag.Slug)).ToList(),
        UtcTimestamp.ToOffset(post.ScheduledPublishAt),
        UtcTimestamp.ToOffset(post.ScheduledUnpublishAt),
        PublicationSchedule.GetState(
            post.IsPublished,
            UtcTimestamp.ToOffset(post.ScheduledPublishAt),
            UtcTimestamp.ToOffset(post.ScheduledUnpublishAt)),
        post.HasBeenPublished)
    {
        WordCount = post.WordCount
    };

    private static PageDto ToPageDto(Page page) => new(
        page.Id,
        page.Title,
        page.Slug,
        page.Content,
        page.IsPublished,
        UtcTimestamp.ToOffset(page.CreatedAt),
        UtcTimestamp.ToOffset(page.UpdatedAt),
        page.SeoTitle,
        page.SeoDescription,
        page.SeoKeywords,
        page.OgImageUrl,
        UtcTimestamp.ToOffset(page.ScheduledPublishAt),
        UtcTimestamp.ToOffset(page.ScheduledUnpublishAt),
        PublicationSchedule.GetState(
            page.IsPublished,
            UtcTimestamp.ToOffset(page.ScheduledPublishAt),
            UtcTimestamp.ToOffset(page.ScheduledUnpublishAt)),
        page.HasBeenPublished);
}
