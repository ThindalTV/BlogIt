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

namespace BlogIt.Tests.Integration;

public class SitemapApiTests
{
    [Fact]
    public async Task Sitemap_UsesConfiguredSiteUrl_IgnoringSpoofedHostHeader()
    {
        await using var db = CreateDb();
        var author = new AppUser { Username = "sitemap-author", DisplayName = "Author", PasswordHash = "x" };
        db.Users.Add(author);
        db.BlogPosts.Add(new BlogPost
        {
            Title = "Post",
            Slug = "post",
            Summary = "Summary",
            IsPublished = true,
            PublishedAt = new DateTime(2025, 1, 2),
            CreatedAt = new DateTime(2025, 1, 2),
            UpdatedAt = new DateTime(2025, 1, 2),
            AuthorId = author.Id,
            Author = author
        });
        await db.SaveChangesAsync();

        var result = await SitemapApi.GetSitemapAsync(
            db,
            Settings(("SiteUrl", "https://real-configured-site.example")),
            Configuration(),
            HttpContext("http", "evil.attacker.example"));

        var body = await ExecuteAsync(result);

        body.Should().Contain("https://real-configured-site.example/");
        body.Should().Contain("https://real-configured-site.example/2025/post");
        body.Should().NotContain("evil.attacker.example");
    }

    [Fact]
    public async Task Sitemap_FallsBackToRequestOrigin_WhenNoSiteUrlConfigured()
    {
        await using var db = CreateDb();

        var result = await SitemapApi.GetSitemapAsync(
            db,
            Settings(),
            Configuration(),
            HttpContext("http", "localhost:5107"));

        var body = await ExecuteAsync(result);

        body.Should().Contain("http://localhost:5107/");
    }

    [Fact]
    public async Task Robots_UsesConfiguredSiteUrl_IgnoringSpoofedHostHeader()
    {
        var result = await SitemapApi.GetRobotsAsync(
            Settings(("SiteUrl", "https://real-configured-site.example")),
            Configuration(),
            HttpContext("http", "evil.attacker.example"),
            new BlogItOptions());

        var body = await ExecuteAsync(result);

        body.Should().Contain("Sitemap: https://real-configured-site.example/sitemap.xml");
        body.Should().NotContain("evil.attacker.example");
    }

    private static BlogItDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"SitemapTests_{Guid.NewGuid():N}")
            .Options;
        return new BlogItDbContext(options);
    }

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

    private static HttpContext HttpContext(string scheme, string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        return context;
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
}
