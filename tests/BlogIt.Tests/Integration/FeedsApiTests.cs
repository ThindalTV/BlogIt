using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Api;
using BlogIt.Services;
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
        // The request PathBase is part of the resolved site URL, so every URL in the feed keeps it.
        entries[0].Element(Atom + "link")!.Attribute("href")!.Value
            .Should().Be($"https://origin.example:8443/site/2025/post-{FeedService.MaxItems + 1}");
        entries[0].Element(Atom + "id")!.Value.Should().StartWith("urn:uuid:");
        entries[0].Element(Atom + "author")!.Element(Atom + "name")!.Value
            .Should().Be("Atom Author");
        document.Root.Elements(Atom + "link")
            .Single(link => link.Attribute("rel")!.Value == "self")
            .Attribute("href")!.Value.Should().Be("https://origin.example:8443/site/atom.xml");
    }

    /// <summary>
    /// Mounting BlogIt under a path prefix is the most likely way a host embeds it, and the site
    /// URL's own path has to survive into every URL the feeds emit — the document link, the
    /// self link and each item link.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/blog")]
    [InlineData("https://example.com/blog/")]
    public async Task Rss_KeepsThePathPrefixOfABlogMountedUnderOne(string configuredSiteUrl)
    {
        await using var db = CreateDb();
        var author = new AppUser
        {
            Username = "prefix-author",
            DisplayName = "Prefix Author",
            PasswordHash = "not-used"
        };
        db.Users.Add(author);
        db.BlogPosts.Add(CreatePost(
            author, "Mounted", "mounted", new DateTime(2025, 5, 6, 7, 0, 0), true));
        await db.SaveChangesAsync();

        var result = await FeedsApi.GetRssAsync(
            db,
            Settings(("SiteUrl", configuredSiteUrl)),
            Configuration(),
            HttpContext("https", "ignored.example"),
            CancellationToken.None);

        var channel = XDocument.Parse((await ExecuteAsync(result)).Body).Root!.Element("channel")!;
        channel.Element("link")!.Value.Should().Be("https://example.com/blog/");
        channel.Element(Atom + "link")!.Attribute("href")!.Value
            .Should().Be("https://example.com/blog/rss.xml");
        channel.Element("item")!.Element("link")!.Value
            .Should().Be("https://example.com/blog/2025/mounted");
    }

    /// <inheritdoc cref="Rss_KeepsThePathPrefixOfABlogMountedUnderOne"/>
    [Fact]
    public async Task Atom_KeepsThePathPrefixOfABlogMountedUnderOne()
    {
        await using var db = CreateDb();
        var author = new AppUser
        {
            Username = "prefix-atom-author",
            DisplayName = "Prefix Atom Author",
            PasswordHash = "not-used"
        };
        db.Users.Add(author);
        db.BlogPosts.Add(CreatePost(
            author, "Mounted", "mounted", new DateTime(2025, 5, 6, 7, 0, 0), true));
        await db.SaveChangesAsync();

        var result = await FeedsApi.GetAtomAsync(
            db,
            Settings(("SiteUrl", "https://example.com/blog/")),
            Configuration(),
            HttpContext("https", "ignored.example"),
            CancellationToken.None);

        var root = XDocument.Parse((await ExecuteAsync(result)).Body).Root!;
        root.Elements(Atom + "link").Single(link => link.Attribute("rel")!.Value == "self")
            .Attribute("href")!.Value.Should().Be("https://example.com/blog/atom.xml");
        root.Element(Atom + "entry")!.Element(Atom + "link")!.Attribute("href")!.Value
            .Should().Be("https://example.com/blog/2025/mounted");
    }

    /// <summary>
    /// A control character pasted into one post must not take the feeds down for every other post:
    /// <see cref="System.Xml.XmlWriterSettings.CheckCharacters"/> defaults to true, so an
    /// unrepresentable character used to throw out of the middle of a half-written document.
    /// </summary>
    [Fact]
    public async Task Feeds_StillRenderWhenOnePostHoldsControlCharacters()
    {
        await using var db = CreateDb();
        var author = new AppUser
        {
            Username = "control-char-author",
            DisplayName = "Vertical\vTab",
            PasswordHash = "not-used"
        };
        db.Users.Add(author);

        var bad = CreatePost(author, "Pasted\vfrom Word", "pasted", new DateTime(2025, 2, 3), true);
        bad.Summary = "A summary with a \v vertical tab";
        bad.Content = "Body with \v a vertical tab and a \u0001 start-of-heading.";
        db.BlogPosts.AddRange(
            bad,
            CreatePost(author, "Clean", "clean", new DateTime(2025, 2, 4), true));
        await db.SaveChangesAsync();

        var settings = Settings(("SiteUrl", "https://blog.example/"));
        var rss = await ExecuteAsync(await FeedsApi.GetRssAsync(
            db, settings, Configuration(), HttpContext("https", "blog.example"),
            CancellationToken.None));
        var atom = await ExecuteAsync(await FeedsApi.GetAtomAsync(
            db, settings, Configuration(), HttpContext("https", "blog.example"),
            CancellationToken.None));

        // Both posts are still there: the offending characters are dropped, not the item.
        var items = XDocument.Parse(rss.Body).Root!.Element("channel")!.Elements("item").ToList();
        items.Select(item => item.Element("title")!.Value)
            .Should().Equal("Clean", "Pastedfrom Word");
        items[1].Element("description")!.Value.Should().Contain("vertical tab");
        items[1].Element(DublinCore + "creator")!.Value.Should().Be("VerticalTab");
        rss.Body.Should().NotContain("\v").And.NotContain("\u0001");

        XDocument.Parse(atom.Body).Root!.Elements(Atom + "entry")
            .Select(entry => entry.Element(Atom + "title")!.Value)
            .Should().Equal("Clean", "Pastedfrom Word");
        atom.Body.Should().NotContain("\v").And.NotContain("\u0001");
    }

    /// <summary>
    /// The other half of the character guard: a rocket emoji is a surrogate pair, which is
    /// perfectly representable in XML and must survive, while a half of one left behind by a
    /// truncating editor is not and must go. Checking a character at a time cannot tell those
    /// apart, so this is the case a naive strip breaks - silently, and in every feed.
    /// </summary>
    [Fact]
    public async Task Feeds_KeepEmojiButDropHalfOfASurrogatePair()
    {
        await using var db = CreateDb();
        var author = new AppUser
        {
            Username = "emoji-author",
            DisplayName = "Emoji Author",
            PasswordHash = "not-used"
        };
        db.Users.Add(author);

        var post = CreatePost(author, "Ship it \U0001F680", "ship-it", new DateTime(2025, 4, 5), true);
        post.Summary = "Launched \U0001F680 today";
        // A lone high surrogate: what a title truncated mid-pair by a fixed-width column leaves.
        post.Content = "Truncated \ud83d here";
        db.BlogPosts.Add(post);
        await db.SaveChangesAsync();

        var settings = Settings(("SiteUrl", "https://blog.example/"));
        var rss = await ExecuteAsync(await FeedsApi.GetRssAsync(
            db, settings, Configuration(), HttpContext("https", "blog.example"),
            CancellationToken.None));
        var atom = await ExecuteAsync(await FeedsApi.GetAtomAsync(
            db, settings, Configuration(), HttpContext("https", "blog.example"),
            CancellationToken.None));

        var item = XDocument.Parse(rss.Body).Root!.Element("channel")!.Element("item")!;
        item.Element("title")!.Value.Should().Be("Ship it \U0001F680");
        item.Element("description")!.Value.Should().Contain("Launched \U0001F680 today");
        item.Element(Content + "encoded")!.Value.Should().Contain("Truncated  here");

        XDocument.Parse(atom.Body).Root!.Element(Atom + "entry")!
            .Element(Atom + "title")!.Value.Should().Be("Ship it \U0001F680");
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
