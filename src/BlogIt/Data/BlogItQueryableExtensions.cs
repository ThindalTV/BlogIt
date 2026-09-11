using BlogIt.Shared.Entities;

namespace BlogIt.Shared.Data;

/// <summary>
/// Query building blocks for hosts reading content straight from <see cref="BlogItDbContext"/>.
/// </summary>
/// <remarks>
/// The data model is public on purpose, which means hosts are invited to write their own queries —
/// and the one thing they must not have to re-derive is what "published" means. Every host that
/// wrote <c>IsPublished == true</c> and stopped there published posts no BlogIt listing returns.
/// These extensions are that rule, in the same namespace as the context so a host that can reach
/// the entities can already reach them.
/// </remarks>
public static class BlogItQueryableExtensions
{
    /// <summary>
    /// Narrows to posts a visitor may see: published <em>and</em> carrying a publication instant.
    /// </summary>
    /// <remarks>
    /// Both halves are load-bearing. <c>IsPublished</c> alone would include a post whose
    /// <c>PublishedAt</c> was never set — a state the admin API can produce — and a post scheduled
    /// for later has neither. Composes into the caller's query and matches the covering index on
    /// <c>(IsPublished, PublishedAt DESC)</c>, so ordering by <c>PublishedAt</c> descending after
    /// this stays a single index read.
    /// </remarks>
    public static IQueryable<BlogPost> WherePublished(this IQueryable<BlogPost> posts) =>
        posts.Where(post => post.IsPublished && post.PublishedAt != null);

    /// <summary>
    /// Narrows to pages a visitor may see.
    /// </summary>
    /// <remarks>
    /// Deliberately not the same rule as the post overload: a page has no publication instant, so
    /// the flag is the whole condition. That asymmetry is the reason this overload exists rather
    /// than being left for hosts to infer.
    /// </remarks>
    public static IQueryable<Page> WherePublished(this IQueryable<Page> pages) =>
        pages.Where(page => page.IsPublished);
}
