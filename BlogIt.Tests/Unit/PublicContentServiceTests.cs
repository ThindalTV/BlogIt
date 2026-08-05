using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Web.Services;
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
        var search = await service.SearchPostsAsync("Newest");
        var tagged = await service.GetPostsByTagAsync("c-sharp", 1, 10);

        page.Posts.Should().ContainSingle().Which.Title.Should().Be("Newest");
        page.TotalPages.Should().Be(2);
        search.Should().ContainSingle().Which.Slug.Should().Be("newest");
        tagged.TagName.Should().Be("C Sharp");
        tagged.Posts.Select(post => post.Title).Should().Equal("Newest", "Older");
        tagged.Posts.Should().OnlyContain(post => post.AuthorDisplayName == "Author");
    }

    [Fact]
    public async Task ContentQueries_ReturnDraftsForPreviewAuthorizationAndNavigationForPublishedPosts()
    {
        var (service, factory) = CreateService();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = CreateAuthor();
            db.BlogPosts.AddRange(
                CreatePost(author, "Older", "older", new DateTime(2025, 1, 1)),
                CreatePost(author, "Current", "current", new DateTime(2025, 2, 1)),
                CreatePost(author, "Newer", "newer", new DateTime(2025, 3, 1)),
                CreatePost(author, "Draft", "draft", null));
            db.Pages.Add(new Page
            {
                Title = "Draft page",
                Slug = "draft-page",
                Content = "Private",
                IsPublished = false
            });
            await db.SaveChangesAsync();
        }

        var published = await service.GetPostAsync("current", includeNavigation: true);
        var draft = await service.GetPostAsync("draft", includeNavigation: false);
        var page = await service.GetPageAsync("draft-page");

        published.Should().NotBeNull();
        published!.PreviousPost!.Slug.Should().Be("older");
        published.NextPost!.Slug.Should().Be("newer");
        draft.Should().NotBeNull();
        draft!.Post.IsPublished.Should().BeFalse();
        page.Should().NotBeNull();
        page!.IsPublished.Should().BeFalse();
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
