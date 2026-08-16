using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace BlogIt.Services;

/// <summary>One entry of a sitemap: a single crawlable URL and when it last changed.</summary>
/// <param name="Path">
/// The site-relative path, always starting with <c>/</c> — for example <c>/2025/hello-world</c>.
/// Use this when you re-base BlogIt's URLs onto a different origin.
/// </param>
/// <param name="Location">
/// The absolute URL to put in a <c>&lt;loc&gt;</c> element. Built from the resolved site URL with
/// its path intact, so a blog mounted at <c>https://example.com/blog/</c> yields
/// <c>https://example.com/blog/2025/hello-world</c>.
/// </param>
/// <param name="LastModified">
/// The value for <c>&lt;lastmod&gt;</c>, or <see langword="null"/> for entries that have no
/// meaningful modification date (the home and archive listings).
/// </param>
public sealed record SitemapEntry(string Path, string Location, DateTime? LastModified);

/// <summary>One <c>User-agent</c> group of a <c>robots.txt</c> document.</summary>
/// <param name="UserAgent">The crawler this group applies to; <c>*</c> means all of them.</param>
/// <param name="Allow">Site-relative paths to allow, in the order they should be written.</param>
/// <param name="Disallow">Site-relative paths to disallow, in the order they should be written.</param>
public sealed record RobotsRuleGroup(
    string UserAgent,
    IReadOnlyList<string> Allow,
    IReadOnlyList<string> Disallow);

/// <summary>
/// Everything BlogIt would put in a <c>robots.txt</c>, as data rather than text.
/// </summary>
/// <remarks>
/// A host that already owns <c>/robots.txt</c> reads this and merges the groups into its own
/// document, which is the only way to express a rule BlogIt does not generate. BlogIt itself only
/// ever contributes one group — keeping the crawler away from the admin portal — so merging is
/// concatenation, not reconciliation.
/// </remarks>
/// <param name="Groups">The <c>User-agent</c> groups BlogIt contributes.</param>
/// <param name="SitemapUrls">
/// Absolute URLs for <c>Sitemap:</c> lines. Empty when <see cref="BlogItOptions.ServeSitemap"/> is
/// off, because BlogIt will not serve the document it would otherwise be pointing at — advertise
/// your own sitemap URL instead.
/// </param>
public sealed record RobotsDirectives(
    IReadOnlyList<RobotsRuleGroup> Groups,
    IReadOnlyList<string> SitemapUrls);

/// <summary>One item of a syndication feed. All timestamps are UTC.</summary>
/// <param name="Id">The post's identifier, for correlating back to <c>IPublicContentService</c>.</param>
/// <param name="Title">The post title, unescaped — escape it for whatever format you write.</param>
/// <param name="Path">
/// The site-relative path of the post, always starting with <c>/</c>. Deliberately relative: see
/// the remarks on <see cref="BlogFeed"/>.
/// </param>
/// <param name="StableId">
/// A permanent, opaque identifier (<c>urn:uuid:…</c>) for the RSS <c>guid</c> / Atom <c>id</c>.
/// Stable across slug and URL changes, which is why it is not the item's link.
/// </param>
/// <param name="SummaryHtml">The summary, already rendered from Markdown to HTML.</param>
/// <param name="ContentHtml">
/// The full body rendered to HTML, falling back to the summary for posts that have no body.
/// </param>
/// <param name="Author">The author's display name, or <see langword="null"/> when unknown.</param>
/// <param name="PublishedAt">When the post was published, UTC.</param>
/// <param name="UpdatedAt">
/// When the post last changed, UTC. Never earlier than <paramref name="PublishedAt"/>.
/// </param>
public sealed record BlogFeedItem(
    Guid Id,
    string Title,
    string Path,
    string StableId,
    string SummaryHtml,
    string ContentHtml,
    string? Author,
    DateTime PublishedAt,
    DateTime UpdatedAt);

/// <summary>Channel-level feed metadata plus the most recent published items.</summary>
/// <remarks>
/// Items carry a site-relative <see cref="BlogFeedItem.Path"/> rather than an absolute URL, and
/// this record carries <see cref="SiteUrl"/> separately. That split is deliberate: combining a
/// path with a site URL is only correct if the site URL's own path is preserved, and getting that
/// wrong silently drops the prefix of a blog mounted under one. Rather than publish one particular
/// combination as the contract, the pieces are handed over separately — concatenate
/// <c>SiteUrl.TrimEnd('/') + item.Path</c> to get an absolute URL.
/// </remarks>
/// <param name="Title">The site name, or <c>BlogIt</c> when unset.</param>
/// <param name="Description">The site description, or a generated one when unset.</param>
/// <param name="SiteUrl">The resolved absolute site URL, with a trailing slash.</param>
/// <param name="UpdatedAt">
/// The newest item's update time, UTC; <see cref="DateTime.UnixEpoch"/> when there are no items.
/// </param>
/// <param name="Items">The items, newest first.</param>
public sealed record BlogFeed(
    string Title,
    string Description,
    string SiteUrl,
    DateTime UpdatedAt,
    IReadOnlyList<BlogFeedItem> Items);

/// <summary>
/// The contents of BlogIt's four root documents — <c>/rss.xml</c>, <c>/atom.xml</c>,
/// <c>/sitemap.xml</c> and <c>/robots.txt</c> — as structured data.
/// </summary>
/// <remarks>
/// <para>
/// Each of those documents can be switched off (see <see cref="BlogItOptions.ServeRssFeed"/> and
/// friends) because most sites already own those URLs. This service is what makes switching them
/// off practical: read the entries here and write them into your own endpoint, your own combined
/// sitemap, or your own <c>robots.txt</c>, instead of losing the content along with the route.
/// </para>
/// <para>
/// The built-in endpoints render exactly this data, so a host-owned document built from it is
/// identical to the one BlogIt would have served — see <c>SitemapApi.RenderSitemap</c>,
/// <c>SitemapApi.RenderRobots</c>, <c>FeedService.CreateRss</c> and <c>FeedService.CreateAtom</c>
/// if you want BlogIt's rendering as well as its data.
/// </para>
/// <para>
/// Like <see cref="IPublicContentService"/>, everything here is published-only, with no opt-out:
/// these documents are crawler-facing by definition, so there is no legitimate reason to leak a
/// draft into one.
/// </para>
/// </remarks>
public interface ISiteMetadataService
{
    /// <summary>
    /// The syndication feed: channel metadata plus the most recently published posts, newest
    /// first.
    /// </summary>
    /// <param name="maxItems">
    /// How many items to include. Defaults to <see cref="FeedService.MaxItems"/>, which is what
    /// the built-in <c>/rss.xml</c> and <c>/atom.xml</c> use.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<BlogFeed> GetFeedAsync(
        int maxItems = FeedService.MaxItems,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every URL BlogIt would list in its sitemap: the home and archive listings, each published
    /// post, and each published page.
    /// </summary>
    /// <remarks>
    /// Ordering is not part of the contract. Sort the result if your own sitemap needs a
    /// particular order.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<IReadOnlyList<SitemapEntry>> GetSitemapEntriesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>The <c>robots.txt</c> rules BlogIt contributes for this site.</summary>
    /// <param name="cancellationToken">Cancels the settings lookup.</param>
    Task<RobotsDirectives> GetRobotsDirectivesAsync(
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISiteMetadataService"/>
public sealed class SiteMetadataService(
    IDbContextFactory<BlogItDbContext> dbContextFactory,
    ISettingsService settings,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    BlogItOptions options) : ISiteMetadataService
{
    /// <inheritdoc/>
    public async Task<BlogFeed> GetFeedAsync(
        int maxItems = FeedService.MaxItems,
        CancellationToken cancellationToken = default)
    {
        var siteUrl = await ResolveSiteUrlAsync();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadFeedAsync(
            db,
            siteUrl,
            await settings.GetAsync(SettingKeys.SiteName),
            await settings.GetAsync(SettingKeys.SiteDescription),
            maxItems,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SitemapEntry>> GetSitemapEntriesAsync(
        CancellationToken cancellationToken = default)
    {
        var siteUrl = await ResolveSiteUrlAsync();
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadSitemapEntriesAsync(db, siteUrl, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<RobotsDirectives> GetRobotsDirectivesAsync(
        CancellationToken cancellationToken = default) =>
        BuildRobotsDirectives(await ResolveSiteUrlAsync(), options);

    /// <summary>
    /// Resolves the site URL the way the endpoints do, except that there may be no request at all:
    /// this service is public and a host may well call it from a background job or a cache warmup.
    /// <see cref="SiteUrlResolver"/> then has nothing to fall back to and says so, which is a far
    /// better failure than emitting URLs pointing at <c>http://</c>.
    /// </summary>
    private async Task<string> ResolveSiteUrlAsync() => SiteUrlResolver.Resolve(
        await settings.GetAsync(SettingKeys.SiteUrl),
        configuration[SettingKeys.SiteUrl],
        httpContextAccessor.HttpContext?.Request);

    /// <summary>
    /// The single loader behind both <see cref="ISiteMetadataService.GetFeedAsync"/> and the
    /// built-in feed endpoints, so the data API cannot drift from what the endpoints render.
    /// </summary>
    internal static async Task<BlogFeed> LoadFeedAsync(
        BlogItDbContext db,
        string siteUrl,
        string? siteName,
        string? siteDescription,
        int maxItems,
        CancellationToken cancellationToken)
    {
        var posts = await db.BlogPosts
            .Where(post => post.IsPublished && post.PublishedAt != null)
            .OrderByDescending(post => post.PublishedAt)
            .ThenByDescending(post => post.Id)
            .Select(post => new
            {
                post.Id,
                post.Title,
                post.Slug,
                post.Summary,
                post.Content,
                PublishedAt = post.PublishedAt!.Value,
                post.CreatedAt,
                post.UpdatedAt,
                Author = post.Author == null ? null : post.Author.DisplayName
            })
            .AsNoTracking()
            .Take(Math.Max(0, maxItems))
            .ToListAsync(cancellationToken);

        var items = posts.Select(post =>
        {
            var publishedAt = ToUtc(post.PublishedAt);
            var updatedAt = ToUtc(post.UpdatedAt);
            if (updatedAt < publishedAt)
                updatedAt = publishedAt;

            return new BlogFeedItem(
                post.Id,
                post.Title,
                BlogUrlHelper.GetPostPath(post.Slug, post.PublishedAt, post.CreatedAt),
                $"urn:uuid:{post.Id:D}",
                MarkdownHelper.ToHtml(post.Summary),
                MarkdownHelper.ToHtml(post.Content ?? post.Summary),
                post.Author,
                publishedAt,
                updatedAt);
        }).ToList();

        var title = string.IsNullOrWhiteSpace(siteName) ? "BlogIt" : siteName;
        return new BlogFeed(
            title,
            string.IsNullOrWhiteSpace(siteDescription)
                ? $"Latest posts from {title}"
                : siteDescription,
            siteUrl,
            items.Count == 0 ? DateTime.UnixEpoch : items.Max(item => item.UpdatedAt),
            items);
    }

    /// <summary>
    /// The single loader behind both <see cref="ISiteMetadataService.GetSitemapEntriesAsync"/> and
    /// the built-in <c>/sitemap.xml</c>.
    /// </summary>
    internal static async Task<IReadOnlyList<SitemapEntry>> LoadSitemapEntriesAsync(
        BlogItDbContext db,
        string siteUrl,
        CancellationToken cancellationToken)
    {
        var baseUrl = siteUrl.TrimEnd('/');

        var posts = await db.BlogPosts
            .Where(post => post.IsPublished && post.PublishedAt != null)
            .Select(post => new { post.Slug, post.PublishedAt, post.CreatedAt, post.UpdatedAt })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var pages = await db.Pages
            .Where(page => page.IsPublished)
            .Select(page => new { page.Slug, page.UpdatedAt })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var entries = new List<SitemapEntry>(posts.Count + pages.Count + 2)
        {
            Entry("/", null),
            Entry("/archive", null)
        };

        entries.AddRange(posts.Select(post => Entry(
            BlogUrlHelper.GetPostPath(post.Slug, post.PublishedAt, post.CreatedAt),
            post.UpdatedAt)));
        entries.AddRange(pages.Select(page => Entry($"/{page.Slug}", page.UpdatedAt)));

        return entries;

        // Concatenation rather than `new Uri(base, path)`: the Uri overload resolves a rooted path
        // against the *origin*, which silently drops the prefix of a blog mounted at
        // https://example.com/blog/.
        SitemapEntry Entry(string path, DateTime? lastModified) =>
            new(path, baseUrl + path, lastModified);
    }

    /// <summary>
    /// The single builder behind both <see cref="ISiteMetadataService.GetRobotsDirectivesAsync"/>
    /// and the built-in <c>/robots.txt</c>.
    /// </summary>
    internal static RobotsDirectives BuildRobotsDirectives(string siteUrl, BlogItOptions options)
    {
        var baseUrl = siteUrl.TrimEnd('/');

        return new RobotsDirectives(
            [new RobotsRuleGroup("*", ["/"], [$"{options.AdminPath.TrimEnd('/')}/"])],
            // Only advertise the sitemap BlogIt actually serves. A host that turned the endpoint
            // off is expected to add its own Sitemap: line for whatever document replaced it;
            // pointing crawlers at a URL nothing answers would be worse than saying nothing.
            options.ServeSitemap ? [$"{baseUrl}/sitemap.xml"] : []);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
