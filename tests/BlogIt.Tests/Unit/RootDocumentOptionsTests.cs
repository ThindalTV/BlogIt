using BlogIt.Shared.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers the opt-out switches for the four documents BlogIt serves at the site root. The
/// assertions deliberately inspect the registered route table rather than issuing requests: the
/// complaint these options answer is that BlogIt <em>claims</em> the URL — a handler that answered
/// 404 would still collide with a host endpoint on the same path, so "returns 404" is not the
/// property under test.
/// </summary>
public class RootDocumentOptionsTests
{
    private static readonly string[] AllRootPatterns =
        ["/rss.xml", "/atom.xml", "/sitemap.xml", "/robots.txt"];

    [Fact]
    public void Options_ServeEveryRootDocumentByDefault()
    {
        var options = new BlogItOptions();

        options.ServeRssFeed.Should().BeTrue();
        options.ServeAtomFeed.Should().BeTrue();
        options.ServeSitemap.Should().BeTrue();
        options.ServeRobotsTxt.Should().BeTrue();
    }

    [Fact]
    public void Options_RejectRootDocumentChangesAfterAddBlogIt()
    {
        var services = new ServiceCollection();
        BlogItOptions? captured = null;
        services.AddBlogIt(options =>
        {
            captured = options;
            AddFakeProviders(options);
        });

        var action = () => captured!.ServeRssFeed = false;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be changed after AddBlogIt*");
    }

    [Fact]
    public async Task MapBlogIt_MapsEveryRootDocument_WithDefaultOptions()
    {
        var patterns = await MapPatternsAsync(_ => { });

        patterns.Should().Contain(AllRootPatterns);
    }

    [Theory]
    [InlineData("/rss.xml")]
    [InlineData("/atom.xml")]
    [InlineData("/sitemap.xml")]
    [InlineData("/robots.txt")]
    public async Task MapBlogIt_DoesNotRegisterTheRoute_WhenItsDocumentIsDisabled(string disabled)
    {
        var patterns = await MapPatternsAsync(options => Disable(options, disabled));

        patterns.Should().NotContain(disabled);
        patterns.Should().Contain(AllRootPatterns.Where(pattern => pattern != disabled));
    }

    [Fact]
    public async Task MapBlogIt_LeavesTheSiteRootUnclaimed_WhenEveryRootDocumentIsDisabled()
    {
        var patterns = await MapPatternsAsync(options =>
        {
            options.ServeRssFeed = false;
            options.ServeAtomFeed = false;
            options.ServeSitemap = false;
            options.ServeRobotsTxt = false;
        });

        patterns.Should().NotIntersectWith(AllRootPatterns);
        // The rest of the engine must be untouched by the opt-outs.
        patterns.Should().Contain("/api/setup/status");
    }

    [Fact]
    public async Task MapBlogIt_PrefixesTheRootDocumentEndpointNames()
    {
        var names = await MapEndpointNamesAsync();

        names.Should().Contain(BlogItEndpointNames.RssFeed);
        names.Should().Contain(BlogItEndpointNames.AtomFeed);
        names.Should().Contain(BlogItEndpointNames.Sitemap);
        names.Should().Contain(BlogItEndpointNames.RobotsTxt);
        // The whole point of the prefix: a host is free to own the unqualified names.
        names.Should().NotContain("RssFeed");
        names.Should().NotContain("AtomFeed");
        names.Should().OnlyContain(name => name.StartsWith("BlogIt.", StringComparison.Ordinal));
    }

    private static void Disable(BlogItOptions options, string pattern)
    {
        switch (pattern)
        {
            case "/rss.xml":
                options.ServeRssFeed = false;
                break;
            case "/atom.xml":
                options.ServeAtomFeed = false;
                break;
            case "/sitemap.xml":
                options.ServeSitemap = false;
                break;
            case "/robots.txt":
                options.ServeRobotsTxt = false;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "Unknown root document.");
        }
    }

    private static async Task<List<string>> MapPatternsAsync(Action<BlogItOptions> configure) =>
        (await MapEndpointsAsync(configure))
            .Select(endpoint => endpoint.RoutePattern.RawText!)
            .ToList();

    private static async Task<List<string>> MapEndpointNamesAsync() =>
        (await MapEndpointsAsync(_ => { }))
            .Select(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName)
            .OfType<string>()
            .ToList();

    private static async Task<List<RouteEndpoint>> MapEndpointsAsync(Action<BlogItOptions> configure)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddBlogIt(options =>
        {
            configure(options);
            AddFakeProviders(options);
        });
        await using var app = builder.Build();

        app.MapBlogIt();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static void AddFakeProviders(BlogItOptions options)
    {
        options.UseDatabaseProvider(new FakeDatabaseProvider());
        options.UseStorageProvider(new FakeStorageProvider());
    }

    private sealed record FakeDatabaseProvider : IBlogItDatabaseProviderRegistration
    {
        public string Name => "fake-db";

        public void RegisterServices(IServiceCollection services) =>
            services.AddDbContextFactory<BlogItDbContext>(options =>
                options.UseInMemoryDatabase($"RootDocuments_{Guid.NewGuid():N}"));
    }

    private sealed record FakeStorageProvider : IBlogItStorageProviderRegistration
    {
        public string Name => "fake-storage";

        public void RegisterServices(IServiceCollection services) =>
            services.AddSingleton<IBlogItMediaStorage, FakeStorage>();
    }

    private sealed class FakeStorage : IBlogItMediaStorage
    {
        public Task<string> StoreAsync(
            Stream content,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<Stream?> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
