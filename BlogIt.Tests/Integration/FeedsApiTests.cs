using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Web.Api;
using BlogIt.Web.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Xml.Linq;

namespace BlogIt.Tests.Integration;

public class FeedsApiTests
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";

    [Fact]
    public async Task Rss_ReturnsNewestPublishedPostsAsValidXml()
    {
        await using var db = CreateDb();
        var author = new AppUser
        {
            Username = "feed-author",
            DisplayName = "A & B",
            PasswordHash = "not-used"
        };
        db.Users.Add(author);

        var older = CreatePost(author, "Older", "older", new DateTime(2024, 12, 1, 8, 0, 0), true);
        var newest = CreatePost(
            author, "News <Today>", "news-today", new DateTime(2025, 1, 2, 10, 0, 0), true);
        newest.Summary = "A **bold** & useful summary";
        newest.Content = "# Full story\n\nThe complete <em>post</em>.";
        db.BlogPosts.AddRange(
            older,
            newest,
            CreatePost(author, "Draft", "draft", new DateTime(2026, 1, 1), false),
            CreatePost(author, "Missing date", "missing-date", null, true));
        await db.SaveChangesAsync();

        var result = await FeedsApi.GetRssAsync(
            db,
            Settings(
                ("SiteUrl", "https://blog.example/"),
                ("SiteName", "Blog & Notes"),
                ("SiteDescription", "Updates <weekly>")),
            Configuration(),
            HttpContext("http", "fallback.invalid"),
            CancellationToken.None);

        var response = await ExecuteAsync(result);
        response.ContentType.Should().Be(FeedsApi.RssContentType);
        var document = XDocument.Parse(response.Body);
        var channel = document.Root!.Element("channel")!;
        channel.Element("title")!.Value.Should().Be("Blog & Notes");
        channel.Element("description")!.Value.Should().Be("Updates <weekly>");

        var items = channel.Elements("item").ToList();
        items.Select(item => item.Element("title")!.Value).Should().Equal("News <Today>", "Older");
        items[0].Element("link")!.Value.Should().Be("https://blog.example/2025/news-today");
        items[0].Element("guid")!.Value.Should().Be($"urn:uuid:{newest.Id:D}");
        items[0].Element("guid")!.Attribute("isPermaLink")!.Value.Should().Be("false");
        items[0].Element("description")!.Value.Should().Contain("<strong>bold</strong>");
        items[0].Element(Content + "encoded")!.Value.Should().Contain(">Full story</h1>");
        items[0].Element(DublinCore + "creator")!.Value.Should().Be("A & B");
        DateTimeOffset.Parse(items[0].Element("pubDate")!.Value).UtcDateTime
            .Should().Be(new DateTime(2025, 1, 2, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Atom_UsesRequestOriginFallbackAndLimitsItems()
    {
        await using var db = CreateDb();
        var author = new AppUser
        {
            Username = "atom-author",
            DisplayName = "Atom Author",
            PasswordHash = "not-used"
        };
        db.Users.Add(author);

        for (var index = 0; index < FeedService.MaxItems + 2; index++)
        {
            db.BlogPosts.Add(CreatePost(
                author,
                $"Post {index}",
                $"post-{index}",
                new DateTime(2025, 1, 1).AddDays(index),
                true));
        }
        await db.SaveChangesAsync();

        var result = await FeedsApi.GetAtomAsync(
            db,
            Settings(("SiteUrl", "not a URL")),
            Configuration(),
            HttpContext("https", "origin.example:8443", "/site"),
            CancellationToken.None);

        var response = await ExecuteAsync(result);
        response.ContentType.Should().Be(FeedsApi.AtomContentType);
        var document = XDocument.Parse(response.Body);
        var entries = document.Root!.Elements(Atom + "entry").ToList();
        entries.Should().HaveCount(FeedService.MaxItems);
        entries[0].Element(Atom + "title")!.Value.Should().Be($"Post {FeedService.MaxItems + 1}");
        entries[0].Element(Atom + "link")!.Attribute("href")!.Value
            .Should().Be($"https://origin.example:8443/2025/post-{FeedService.MaxItems + 1}");
        entries[0].Element(Atom + "id")!.Value.Should().StartWith("urn:uuid:");
        entries[0].Element(Atom + "author")!.Element(Atom + "name")!.Value
            .Should().Be("Atom Author");
        document.Root.Elements(Atom + "link")
            .Single(link => link.Attribute("rel")!.Value == "self")
            .Attribute("href")!.Value.Should().Be("https://origin.example:8443/atom.xml");
    }

    private static BlogItDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"FeedTests_{Guid.NewGuid():N}")
            .Options;
        return new BlogItDbContext(options);
    }

    private static BlogPost CreatePost(
        AppUser author,
        string title,
        string slug,
        DateTime? publishedAt,
        bool published) => new()
        {
            Title = title,
            Slug = slug,
            Summary = "Summary",
            Content = "Content",
            IsPublished = published,
            PublishedAt = publishedAt,
            CreatedAt = publishedAt ?? new DateTime(2020, 1, 1),
            UpdatedAt = publishedAt ?? new DateTime(2020, 1, 1),
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

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    private static DefaultHttpContext HttpContext(
        string scheme,
        string host,
        string pathBase = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        context.Request.PathBase = pathBase;
        return context;
    }

    private static async Task<(string Body, string? ContentType)> ExecuteAsync(IResult result)
    {
        var context = new DefaultHttpContext();
        await using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.RequestServices = services;
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await result.ExecuteAsync(context);
        body.Position = 0;
        using var reader = new StreamReader(body);
        return (await reader.ReadToEndAsync(), context.Response.ContentType);
    }
}
