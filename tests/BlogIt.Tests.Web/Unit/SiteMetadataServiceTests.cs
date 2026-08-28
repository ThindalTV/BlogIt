using BlogIt.Api;
using BlogIt.Services;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers <see cref="ISiteMetadataService"/>, the data surface a host uses when it opts out of
/// BlogIt's own root documents. The equivalence tests render the service's data through the same
/// renderers the built-in endpoints use and compare byte-for-byte, so "opt out and rebuild it
/// yourself" provably produces the same document rather than an approximation of it.
/// </summary>
public class SiteMetadataServiceTests
{
    [Fact]
    public async Task Sitemap_DataMatchesWhatTheBuiltInEndpointRenders()
    {
        var context = await CreateAsync("https://blog.example/");

        var entries = await context.Service.GetSitemapEntriesAsync();
        var endpointBody = await ExecuteAsync(await SitemapApi.GetSitemapAsync(
            context.Db, context.Settings, context.Configuration, context.Http));

        SitemapApi.RenderSitemap(entries).Should().Be(endpointBody);
        // Order-insensitive: the sitemap query has never ordered its rows, and pinning an order
        // here would assert a database-provider detail rather than the contract.
        entries.Select(entry => entry.Path).Should().BeEquivalentTo(
            ["/", "/archive", "/2025/newest", "/2024/older", "/about"]);
        entries.Should().OnlyContain(entry =>
            entry.Location == "https://blog.example" + entry.Path);
        entries.Should().NotContain(entry => entry.Path == "/hidden");
        entries.Single(entry => entry.Path == "/about").LastModified
            .Should().Be(new DateTime(2025, 3, 4));
    }

    [Fact]
    public async Task Feed_DataMatchesWhatTheBuiltInEndpointsRender()
    {
        var context = await CreateAsync("https://blog.example/");

        var feed = await context.Service.GetFeedAsync();
        var rss = await FeedService.CreateRssAsync(
            context.Db, context.Settings, context.Configuration, context.Http.Request);
        var atom = await FeedService.CreateAtomAsync(
            context.Db, context.Settings, context.Configuration, context.Http.Request);

        FeedService.CreateRss(feed).Should().Be(rss);
        FeedService.CreateAtom(feed).Should().Be(atom);
        feed.Title.Should().Be("Blog");
        feed.SiteUrl.Should().Be("https://blog.example/");
        feed.Items.Select(item => item.Title).Should().Equal("Newest", "Older");
        feed.Items[0].StableId.Should().StartWith("urn:uuid:");
        feed.Items[0].SummaryHtml.Should().Contain("<p>");
    }

    [Fact]
    public async Task Robots_DataMatchesWhatTheBuiltInEndpointRenders()
    {
        var context = await CreateAsync("https://blog.example/");

        var directives = await context.Service.GetRobotsDirectivesAsync();
        var endpointBody = await ExecuteAsync(await SitemapApi.GetRobotsAsync(
            context.Settings, context.Configuration, context.Http, context.Options));

        SitemapApi.RenderRobots(directives).Should().Be(endpointBody);
        var group = directives.Groups.Should().ContainSingle().Subject;
        group.UserAgent.Should().Be("*");
        group.Allow.Should().Equal("/");
        group.Disallow.Should().Equal("/blogit/");
        directives.SitemapUrls.Should().Equal("https://blog.example/sitemap.xml");
    }

    [Fact]
    public async Task Robots_StopsAdvertisingTheSitemap_WhenBlogItNoLongerServesIt()
    {
        var context = await CreateAsync(
            "https://blog.example/",
            new BlogItOptions { ServeSitemap = false });

        var directives = await context.Service.GetRobotsDirectivesAsync();

        directives.SitemapUrls.Should().BeEmpty();
        directives.Groups.Should().ContainSingle();
    }

    [Fact]
    public async Task Robots_UsesTheConfiguredAdminPath()
    {
        var context = await CreateAsync(
            "https://blog.example/",
            new BlogItOptions { AdminPath = "/manage" });

        var directives = await context.Service.GetRobotsDirectivesAsync();

        directives.Groups[0].Disallow.Should().Equal("/manage/");
    }

    [Fact]
    public async Task Entries_StayUsableWhenTheBlogIsMountedUnderAPathPrefix()
    {
        var context = await CreateAsync("https://host.example/blog/");

        var entries = await context.Service.GetSitemapEntriesAsync();
        var feed = await context.Service.GetFeedAsync();
        var directives = await context.Service.GetRobotsDirectivesAsync();

        entries.Single(entry => entry.Path == "/2025/newest").Location
            .Should().Be("https://host.example/blog/2025/newest");
        directives.SitemapUrls.Should().Equal("https://host.example/blog/sitemap.xml");
        // Feed items carry the site-relative path, never a pre-combined absolute URL: combining
        // against the origin is exactly what loses the "/blog" prefix, and that must not be frozen
        // into the public contract.
        feed.SiteUrl.Should().Be("https://host.example/blog/");
        feed.Items.Should().OnlyContain(item => item.Path.StartsWith("/"));
        feed.Items[0].Path.Should().Be("/2025/newest");
    }

    [Fact]
    public async Task GetFeedAsync_HonoursTheRequestedItemCount()
    {
        var context = await CreateAsync("https://blog.example/");

        var feed = await context.Service.GetFeedAsync(1);

        feed.Items.Should().ContainSingle().Which.Title.Should().Be("Newest");
    }

    private sealed record TestContext(
        SiteMetadataService Service,
        BlogItDbContext Db,
        ISettingsService Settings,
        IConfiguration Configuration,
        DefaultHttpContext Http,
        BlogItOptions Options);

    private static async Task<TestContext> CreateAsync(
        string siteUrl,
        BlogItOptions? options = null)
    {
        var dbOptions = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"SiteMetadata_{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(dbOptions);

        var db = await factory.CreateDbContextAsync();
        var author = new AppUser
        {
            Username = "metadata-author",
            DisplayName = "Author",
            PasswordHash = "unused"
        };
        db.Users.Add(author);
        db.BlogPosts.AddRange(
            CreatePost(author, "Older", "older", new DateTime(2024, 12, 1)),
            CreatePost(author, "Newest", "newest", new DateTime(2025, 1, 2)),
            new BlogPost
            {
                Title = "Draft",
                Slug = "draft",
                Summary = "Summary",
                IsPublished = false,
                CreatedAt = new DateTime(2025, 1, 1),
                UpdatedAt = new DateTime(2025, 1, 1),
                AuthorId = author.Id,
                Author = author
            });
        db.Pages.AddRange(
            new Page
            {
                Title = "About",
                Slug = "about",
                Content = "About us",
                IsPublished = true,
                CreatedAt = new DateTime(2025, 3, 4),
                UpdatedAt = new DateTime(2025, 3, 4)
            },
            new Page
            {
                Title = "Hidden",
                Slug = "hidden",
                Content = "Not live",
                IsPublished = false,
                CreatedAt = new DateTime(2025, 3, 4),
                UpdatedAt = new DateTime(2025, 3, 4)
            });
        await db.SaveChangesAsync();

        var settings = Settings(("SiteUrl", siteUrl), ("SiteName", "Blog"));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var http = new DefaultHttpContext();
        http.Request.Scheme = "http";
        http.Request.Host = new HostString("request-origin.invalid");
        var accessor = new HttpContextAccessor { HttpContext = http };
        options ??= new BlogItOptions();

        return new TestContext(
            new SiteMetadataService(factory, settings, configuration, accessor, options),
            db,
            settings,
            configuration,
            http,
            options);
    }

    private static BlogPost CreatePost(AppUser author, string title, string slug, DateTime publishedAt) => new()
    {
        Title = title,
        Slug = slug,
        Summary = $"{title} summary",
        Content = $"{title} content",
        IsPublished = true,
        HasBeenPublished = true,
        PublishedAt = publishedAt,
        CreatedAt = publishedAt,
        UpdatedAt = publishedAt,
        AuthorId = author.Id,
        Author = author
    };

    private static ISettingsService Settings(params (string Key, string Value)[] values)
    {
        var dictionary = values.ToDictionary(pair => pair.Key, pair => pair.Value);
        var settings = new Mock<ISettingsService>();
        settings.Setup(service => service.GetAsync(It.IsAny<string>()))
            .ReturnsAsync((string key) => dictionary.GetValueOrDefault(key));
        return settings.Object;
    }

    private static async Task<string> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.RequestServices = services;
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        return await reader.ReadToEndAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<BlogItDbContext> options)
        : IDbContextFactory<BlogItDbContext>
    {
        public BlogItDbContext CreateDbContext() => new(options);

        public Task<BlogItDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
