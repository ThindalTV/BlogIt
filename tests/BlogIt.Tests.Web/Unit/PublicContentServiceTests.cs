using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

public class PublicContentServiceTests
{
    [Fact]
    public async Task PublicPostQueries_ExcludeDraftsAndReturnDtoData()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = CreateAuthor();
            var tag = new Tag { Name = "C Sharp", Slug = "c-sharp" };
            db.BlogPosts.AddRange(
                CreatePost(author, "Older", "older", new DateTime(2025, 1, 1), tag),
                CreatePost(author, "Newest", "newest", new DateTime(2025, 2, 1), tag),
                CreatePost(author, "Draft", "draft", null, tag));
            await db.SaveChangesAsync();
        }

        var page = await service.GetPostsAsync(1, 1);
        var search = await service.SearchPostsAsync("Newest", 1, 10);
        var tagged = await service.GetPostsByTagAsync("c-sharp", 1, 10);

        page.Posts.Should().ContainSingle().Which.Title.Should().Be("Newest");
        page.TotalPages.Should().Be(2);
        search.Posts.Should().ContainSingle().Which.Slug.Should().Be("newest");
        tagged.TagName.Should().Be("C Sharp");
        tagged.Posts.Select(post => post.Title).Should().Equal("Newest", "Older");
        tagged.Posts.Should().OnlyContain(post => post.AuthorDisplayName == "Author");
    }

    [Fact]
    public async Task SearchPostsAsync_PaginatesResultsAndExcludesFullContent()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = CreateAuthor();
            var tag = new Tag { Name = "Search", Slug = "search" };
            for (var i = 0; i < 5; i++)
            {
                var post = CreatePost(author, $"Searchable {i}", $"searchable-{i}", new DateTime(2025, 1, 1 + i), tag);
                post.Content = "Full body content that should not come back in search results.";
                db.BlogPosts.Add(post);
            }
            await db.SaveChangesAsync();
        }

        var firstPage = await service.SearchPostsAsync("Searchable", 1, 2);
        firstPage.Posts.Should().HaveCount(2);
        firstPage.TotalPages.Should().Be(3);
        firstPage.Page.Should().Be(1);
        firstPage.Posts.Should().OnlyContain(post => post.HasFullContent);

        var lastPage = await service.SearchPostsAsync("Searchable", 3, 2);
        lastPage.Posts.Should().ContainSingle();
        lastPage.Page.Should().Be(3);

        var beyondLastPage = await service.SearchPostsAsync("Searchable", 99, 2);
        beyondLastPage.Page.Should().Be(3);
        beyondLastPage.Posts.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPostAsync_ReturnsPublishedPostWithNavigation()
    {
        var (service, factory) = CreateService();
        await SeedContentAsync(factory);

        var published = await service.GetPostAsync("current", includeNavigation: true);

        published.Should().NotBeNull();
        published!.PreviousPost!.Slug.Should().Be("older");
        published.NextPost!.Slug.Should().Be("newer");
    }

    [Fact]
    public async Task GetPostAsync_HidesDraftsByDefault()
    {
        // The default has to be safe on its own: a host that renders whatever comes back — which
        // is the obvious way to write a post page — must not publish a draft to anyone who
        // guesses the slug. Opting in is the caller's explicit decision, not the caller's job to
        // remember.
        var (service, factory) = CreateService();
        await SeedContentAsync(factory);

        var draft = await service.GetPostAsync("draft", includeNavigation: false);

        draft.Should().BeNull();
    }

    [Fact]
    public async Task GetPostAsync_HidesPostsScheduledButNotYetLiveByDefault()
    {
        var (service, factory) = CreateService();
        await SeedContentAsync(factory);

        var scheduled = await service.GetPostAsync("scheduled", includeNavigation: false);

        scheduled.Should().BeNull();
    }

    [Fact]
    public async Task GetPostAsync_ReturnsDraftWhenUnpublishedIsExplicitlyRequested()
    {
        // The preview path needs the draft in hand to authorize a token against its id.
        var (service, factory) = CreateService();
        await SeedContentAsync(factory);

        var draft = await service.GetPostAsync("draft", includeNavigation: false, includeUnpublished: true);

        draft.Should().NotBeNull();
        draft!.Post.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task GetPageAsync_HidesUnpublishedPagesByDefault()
    {
        var (service, factory) = CreateService();
        await SeedContentAsync(factory);

        var page = await service.GetPageAsync("draft-page");

        page.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_ReturnsUnpublishedPageWhenExplicitlyRequested()
    {
        var (service, factory) = CreateService();
        await SeedContentAsync(factory);

        var page = await service.GetPageAsync("draft-page", includeUnpublished: true);

        page.Should().NotBeNull();
        page!.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task GetPageAsync_ReturnsPublishedPage()
    {
        var (service, factory) = CreateService();
        await SeedContentAsync(factory);

        var page = await service.GetPageAsync("live-page");

        page.Should().NotBeNull();
        page!.IsPublished.Should().BeTrue();
    }

    private static async Task SeedContentAsync(TestDbContextFactory factory)
    {
        await using var db = await factory.CreateDbContextAsync();
        var author = CreateAuthor();
        var scheduled = CreatePost(author, "Scheduled", "scheduled", null);
        scheduled.ScheduledPublishAt = new DateTime(2030, 1, 1);

        db.BlogPosts.AddRange(
            CreatePost(author, "Older", "older", new DateTime(2025, 1, 1)),
            CreatePost(author, "Current", "current", new DateTime(2025, 2, 1)),
            CreatePost(author, "Newer", "newer", new DateTime(2025, 3, 1)),
            CreatePost(author, "Draft", "draft", null),
            scheduled);
        db.Pages.AddRange(
            new Page
            {
                Title = "Draft page",
                Slug = "draft-page",
                Content = "Private",
                IsPublished = false
            },
            new Page
            {
                Title = "Live page",
                Slug = "live-page",
                Content = "Public",
                IsPublished = true,
                HasBeenPublished = true
            });
        await db.SaveChangesAsync();
    }

    private static (PublicContentService Service, TestDbContextFactory Factory) CreateService()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"PublicContent_{Guid.NewGuid():N}")
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
            AuthorId = author.Id,
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
