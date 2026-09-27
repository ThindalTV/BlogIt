using BlogIt.Services;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers the read surface added so that a host does not have to reach into
/// <see cref="BlogItDbContext"/> to render an ordinary blog: archive queries, result counts, word
/// counts and tag lookup.
/// </summary>
/// <remarks>
/// Each of these existed as host code before it existed here. The archive listing in particular was
/// roughly a hundred lines against the raw context, most of it re-deriving
/// <see cref="Shared.DTOs.BlogPostSummaryDto"/> — including the publication-schedule state, which is
/// engine logic a host has no way to keep in step.
/// </remarks>
public class PublicContentReadSurfaceTests
{
    private static readonly DateTimeOffset March = new(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset April = new(2025, 4, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DateRange_IsHalfOpenSoAdjacentMonthsDoNotDoubleCount()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = CreateAuthor();
            db.BlogPosts.AddRange(
                // Exactly on the lower bound: included.
                CreatePost(author, "OnFrom", "on-from", March.UtcDateTime),
                CreatePost(author, "Middle", "middle", new DateTime(2025, 3, 15)),
                // Exactly on the upper bound: excluded, and belongs to April's page instead.
                CreatePost(author, "OnTo", "on-to", April.UtcDateTime));
            await db.SaveChangesAsync();
        }

        var page = await service.GetPostsByDateRangeAsync(March, April, 1, 10);

        page.Posts.Select(post => post.Slug).Should().BeEquivalentTo("on-from", "middle");
        page.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task DateRange_ExcludesDraftsAndPostsScheduledButNotLive()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = CreateAuthor();
            var scheduled = CreatePost(author, "Scheduled", "scheduled", null);
            scheduled.ScheduledPublishAt = new DateTime(2025, 3, 10);

            // The shape a host that checks only IsPublished would wrongly show: flagged published,
            // but with no publication instant, so no BlogIt listing returns it.
            var flagOnly = CreatePost(author, "FlagOnly", "flag-only", null);
            flagOnly.IsPublished = true;

            db.BlogPosts.AddRange(
                CreatePost(author, "Live", "live", new DateTime(2025, 3, 15)),
                CreatePost(author, "Draft", "draft", null),
                scheduled,
                flagOnly);
            await db.SaveChangesAsync();
        }

        var page = await service.GetPostsByDateRangeAsync(March, April, 1, 10);

        page.Posts.Select(post => post.Slug).Should().Equal("live");
    }

    [Fact]
    public async Task DateRange_InvertedRangeReturnsAnEmptyPageRatherThanThrowing()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BlogPosts.Add(
                CreatePost(CreateAuthor(), "Live", "live", new DateTime(2025, 3, 15)));
            await db.SaveChangesAsync();
        }

        var page = await service.GetPostsByDateRangeAsync(April, March, 1, 10);

        page.Posts.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task DateRange_ReadsAnyOffsetAsTheInstantItNames()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.BlogPosts.Add(CreatePost(
                CreateAuthor(), "Live", "live", new DateTime(2025, 3, 15, 12, 0, 0)));
            await db.SaveChangesAsync();
        }

        // The same window expressed at a different offset must select the same posts, which is the
        // whole reason the parameters are instants rather than a year and a month.
        var utc = await service.GetPostsByDateRangeAsync(March, April, 1, 10);
        var shifted = await service.GetPostsByDateRangeAsync(
            March.ToOffset(TimeSpan.FromHours(2)),
            April.ToOffset(TimeSpan.FromHours(2)),
            1,
            10);

        shifted.Posts.Select(post => post.Slug)
            .Should().Equal(utc.Posts.Select(post => post.Slug));
    }

    [Fact]
    public async Task ArchiveCounts_GroupByUtcMonthNewestFirstAndIgnoreDrafts()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = CreateAuthor();
            db.BlogPosts.AddRange(
                CreatePost(author, "A", "a", new DateTime(2025, 3, 1)),
                CreatePost(author, "B", "b", new DateTime(2025, 3, 28)),
                CreatePost(author, "C", "c", new DateTime(2024, 12, 9)),
                CreatePost(author, "Draft", "draft", null));
            await db.SaveChangesAsync();
        }

        var counts = await service.GetArchiveCountsAsync();

        counts.Should().Equal(
            new PostArchiveCount(2025, 3, 2),
            new PostArchiveCount(2024, 12, 1));
    }

    [Fact]
    public async Task TotalCount_IsTheFullMatchCountNotThePageLength()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = CreateAuthor();
            var tag = new Tag { Name = "Azure", Slug = "azure" };
            for (var index = 0; index < 7; index++)
            {
                db.BlogPosts.Add(CreatePost(
                    author, $"Post {index}", $"post-{index}", new DateTime(2025, 3, 1 + index), tag));
            }

            await db.SaveChangesAsync();
        }

        // Asserted exactly, so returning TotalPages by mistake fails rather than merely looking
        // plausible.
        (await service.GetPostsAsync(1, 2)).TotalCount.Should().Be(7);
        (await service.GetPostsByTagAsync("azure", 1, 2)).TotalCount.Should().Be(7);
        (await service.GetPostsByDateRangeAsync(March, April, 1, 2)).TotalCount.Should().Be(7);
        (await service.SearchPostsAsync("Post", 1, 2)).TotalCount.Should().Be(7);
    }

    [Fact]
    public async Task TotalCount_IsZeroOnTheEmptyPaths()
    {
        var (service, _) = CreateService();

        (await service.SearchPostsAsync("   ", 1, 10)).TotalCount.Should().Be(0);
        (await service.GetPostsByTagAsync("no-such-tag", 1, 10)).TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetTag_DistinguishesAMissingTagFromOneWithNothingPublished()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Tags.Add(new Tag { Name = "Quiet Tag", Slug = "quiet-tag" });
            await db.SaveChangesAsync();
        }

        // GetPostsByTagAsync reports both cases as a null TagName, which is exactly why a host had
        // to run a full paged listing just to learn a display name.
        (await service.GetTagAsync("quiet-tag"))!.Name.Should().Be("Quiet Tag");
        (await service.GetTagAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task WordCount_ReachesListingsWithoutTheBodyAndTracksEdits()
    {
        var (service, factory) = CreateService();
        Guid postId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var post = CreatePost(CreateAuthor(), "Live", "live", new DateTime(2025, 3, 15));
            post.Content = "one two three four five";
            db.BlogPosts.Add(post);
            await db.SaveChangesAsync();
            postId = post.Id;
        }

        // Search projects explicitly and never selects Content, so this is the path that proves the
        // count survives the body being left behind.
        var searched = await service.SearchPostsAsync("Live", 1, 10);
        searched.Posts.Single().WordCount.Should().Be(5);
        searched.Posts.Single().HasFullContent.Should().BeTrue();

        await using (var db = await factory.CreateDbContextAsync())
        {
            var post = await db.BlogPosts.FirstAsync(item => item.Id == postId);
            post.Content = "one two";
            await db.SaveChangesAsync();
        }

        (await service.GetPostsAsync(1, 10)).Posts.Single().WordCount.Should().Be(2);
    }

    [Fact]
    public async Task WordCount_IsZeroForASummaryOnlyPostRatherThanUnknown()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var post = CreatePost(CreateAuthor(), "Link", "link", new DateTime(2025, 3, 15));
            post.Content = null;
            db.BlogPosts.Add(post);
            await db.SaveChangesAsync();
        }

        // Null means "never computed"; a post with no body has been counted and has nothing to read.
        (await service.GetPostsAsync(1, 10)).Posts.Single().WordCount.Should().Be(0);
    }

    [Fact]
    public async Task WordCount_IsNotRecomputedWhenOnlyTheTitleChanges()
    {
        var (_, factory) = CreateService();
        Guid postId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var post = CreatePost(CreateAuthor(), "Live", "live", new DateTime(2025, 3, 15));
            post.Content = "one two three";
            db.BlogPosts.Add(post);
            await db.SaveChangesAsync();
            postId = post.Id;
        }

        await using (var db = await factory.CreateDbContextAsync())
        {
            var post = await db.BlogPosts.FirstAsync(item => item.Id == postId);
            // Set out of band, so a recompute triggered by an unrelated edit would overwrite it.
            post.WordCount = 999;
            post.Title = "Renamed";
            await db.SaveChangesAsync();
        }

        await using (var verify = await factory.CreateDbContextAsync())
        {
            (await verify.BlogPosts.FirstAsync(item => item.Id == postId))
                .WordCount.Should().Be(999);
        }
    }

    private static (PublicContentService Service, TestDbContextFactory Factory) CreateService()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"ReadSurface_{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        return (new PublicContentService(factory), factory);
    }

    private static AppUser CreateAuthor() => new()
    {
        Username = $"author-{Guid.NewGuid():N}",
        DisplayName = "Author",
        PasswordHash = "unused"
    };

    private static BlogPost CreatePost(
        AppUser author,
        string title,
        string slug,
        DateTime? publishedAt,
        Tag? tag = null) => new()
        {
            Title = title,
            Slug = slug,
            Summary = $"{title} summary",
            Content = $"{title} content",
            IsPublished = publishedAt.HasValue,
            HasBeenPublished = publishedAt.HasValue,
            PublishedAt = publishedAt,
            Author = author,
            Tags = tag is null ? [] : [tag]
        };

    private sealed class TestDbContextFactory(DbContextOptions<BlogItDbContext> options)
        : IDbContextFactory<BlogItDbContext>
    {
        public BlogItDbContext CreateDbContext() => new(options);

        public Task<BlogItDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
