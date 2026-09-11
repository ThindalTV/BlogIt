using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers <see cref="BlogItQueryableExtensions"/>, the public expression of what "published" means.
/// </summary>
/// <remarks>
/// The data model is public on purpose, so hosts write their own queries against it — and every one
/// of them previously had to re-derive this rule by reading engine source. Writing
/// <c>IsPublished == true</c> and stopping there returns posts that no BlogIt listing shows.
/// </remarks>
public class PublishedQueryTests
{
    [Fact]
    public async Task WherePublished_RequiresBothTheFlagAndAPublicationInstant()
    {
        await using var db = CreateContext();
        var author = new AppUser
        {
            Username = "author",
            DisplayName = "Author",
            PasswordHash = "unused"
        };

        db.BlogPosts.AddRange(
            Post(author, "both", isPublished: true, publishedAt: new DateTime(2025, 3, 1)),
            // Flagged published but never given an instant. Reachable through the admin API, and
            // exactly the row a host filtering on the flag alone would wrongly publish.
            Post(author, "flag-only", isPublished: true, publishedAt: null),
            // An instant but not published: what unpublishing a live post leaves behind.
            Post(author, "instant-only", isPublished: false, publishedAt: new DateTime(2025, 3, 1)),
            Post(author, "neither", isPublished: false, publishedAt: null));
        await db.SaveChangesAsync();

        var slugs = await db.BlogPosts.WherePublished()
            .Select(post => post.Slug)
            .ToListAsync();

        slugs.Should().Equal("both");
    }

    [Fact]
    public async Task WherePublished_ForPagesIsTheFlagAlone()
    {
        await using var db = CreateContext();
        db.Pages.AddRange(
            new Page { Title = "Live", Slug = "live", Content = "x", IsPublished = true },
            new Page { Title = "Draft", Slug = "draft", Content = "x", IsPublished = false });
        await db.SaveChangesAsync();

        // Deliberately a different rule from the post overload: a page has no publication instant,
        // so requiring one would return nothing at all. The asymmetry is the reason the engine
        // publishes both overloads rather than leaving hosts to infer either.
        var slugs = await db.Pages.WherePublished().Select(page => page.Slug).ToListAsync();

        slugs.Should().Equal("live");
    }

    [Fact]
    public void WherePublished_TranslatesToSqlRatherThanRunningInMemory()
    {
        // Against a real relational provider, with no server involved: ToQueryString forces
        // translation, so a predicate that could only be evaluated client-side fails here rather
        // than quietly loading the whole table in production.
        using var db = new BlogItDbContext(
            new DbContextOptionsBuilder<BlogItDbContext>()
                .UseSqlServer("Server=localhost;Database=BlogItTranslation;User Id=t;Password=t")
                .Options);

        var sql = db.BlogPosts.WherePublished()
            .OrderByDescending(post => post.PublishedAt)
            .ToQueryString();

        sql.Should().Contain("IsPublished");
        sql.Should().Contain("PublishedAt");
        sql.Should().Contain("ORDER BY");
    }

    [Fact]
    public void DateRangeFiltering_TranslatesDespiteTheUtcValueConverter()
    {
        using var db = new BlogItDbContext(
            new DbContextOptionsBuilder<BlogItDbContext>()
                .UseSqlServer("Server=localhost;Database=BlogItTranslation;User Id=t;Password=t")
                .Options);

        var from = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2025, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        // Whole-value range comparison, which the converter permits. Member access on a converted
        // column (.Year, .Month) does not translate, which is why the archive query is shaped as a
        // range and its counts are grouped client-side.
        var sql = db.BlogPosts.WherePublished()
            .Where(post => post.PublishedAt >= from && post.PublishedAt < to)
            .ToQueryString();

        sql.Should().Contain("PublishedAt");
    }

    private static BlogItDbContext CreateContext() => new(
        new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"Published_{Guid.NewGuid():N}")
            .Options);

    private static BlogPost Post(
        AppUser author,
        string slug,
        bool isPublished,
        DateTime? publishedAt) => new()
        {
            Title = slug,
            Slug = slug,
            Summary = "summary",
            IsPublished = isPublished,
            PublishedAt = publishedAt,
            Author = author
        };
}
