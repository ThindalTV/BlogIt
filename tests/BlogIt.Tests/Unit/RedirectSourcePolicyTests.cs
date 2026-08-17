using BlogIt.Api;
using BlogIt.Middleware;
using BlogIt.Services;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers <see cref="BlogItOptions.RedirectSourcePrefixes"/> — the host's control over which URLs a
/// blog author's redirect table may claim. Both halves are tested: the write-time check that
/// answers a 400, and the read-time check in <see cref="UrlRedirectMiddleware"/> that stops rows
/// created before the option was set from still being honoured.
/// </summary>
public class RedirectSourcePolicyTests
{
    [Theory]
    [InlineData("/login")]
    [InlineData("/pricing")]
    [InlineData("/2019/03/15/legacy-wordpress-url")]
    public void TryNormalize_AllowsAnySource_WhenNoPrefixIsConfigured(string source)
    {
        // The documented default. Redirect sources are legacy URLs from whatever the site was
        // before, so they are mostly outside blog-owned space by nature.
        TryNormalize(new BlogItOptions(), source, out var error).Should().BeTrue(error);
    }

    [Theory]
    [InlineData("/blog")]
    [InlineData("/blog/2019/old-post")]
    [InlineData("/BLOG/case-insensitive")]
    public void TryNormalize_AllowsASourceUnderAConfiguredPrefix(string source)
    {
        var options = new BlogItOptions { RedirectSourcePrefixes = ["/blog"] };

        TryNormalize(options, source, out var error).Should().BeTrue(error);
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/pricing")]
    [InlineData("/blogger/sibling-prefix-is-not-inside")]
    public void TryNormalize_RejectsASourceOutsideTheConfiguredPrefixes(string source)
    {
        var options = new BlogItOptions { RedirectSourcePrefixes = ["/blog", "/news"] };

        TryNormalize(options, source, out var error).Should().BeFalse();
        error.Should().Contain("/blog").And.Contain("/news");
    }

    [Fact]
    public void RedirectSourcePrefixes_AreNormalizedOnAssignment()
    {
        var options = new BlogItOptions { RedirectSourcePrefixes = ["blog/", " /news "] };

        options.RedirectSourcePrefixes.Should().Equal("/blog", "/news");
    }

    [Fact]
    public void RedirectSourcePrefixes_RejectAnEmptyEntry()
    {
        var assign = () => new BlogItOptions { RedirectSourcePrefixes = ["/blog", "  "] };

        assign.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RedirectSourcePrefixes_CannotChangeAfterAddBlogIt()
    {
        var services = new ServiceCollection();
        BlogItOptions? captured = null;
        services.AddBlogIt(options =>
        {
            captured = options;
            options.UseDatabaseProvider(new InMemoryDatabaseProvider());
            options.UseStorageProvider(new NoOpStorageProvider());
        });

        var assign = () => captured!.RedirectSourcePrefixes = ["/blog"];

        assign.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot be changed after AddBlogIt*");
    }

    [Theory]
    [InlineData("/rss.xml")]
    [InlineData("/atom.xml")]
    [InlineData("/sitemap.xml")]
    [InlineData("/robots.txt")]
    public void TryNormalize_ReservesARootDocument_OnlyWhileBlogItServesIt(string source)
    {
        TryNormalize(new BlogItOptions(), source, out _).Should().BeFalse();

        var disabled = new BlogItOptions();
        Disable(disabled, source);

        TryNormalize(disabled, source, out var error).Should().BeTrue(error);
    }

    [Fact]
    public void TryNormalize_KeepsReservingTheOtherRootDocuments_WhenOneIsDisabled()
    {
        var options = new BlogItOptions { ServeRssFeed = false };

        TryNormalize(options, "/atom.xml", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Middleware_HonoursAnyRedirect_WhenNoPrefixIsConfigured()
    {
        var context = await InvokeAsync(new BlogItOptions(), "/login");

        context.Response.Headers.Location.ToString().Should().Be("https://evil.example/");
    }

    [Fact]
    public async Task Middleware_IgnoresARedirectOutsideTheConfiguredPrefixes()
    {
        // The row may predate the option, so the write-time check alone would leave it live.
        var options = new BlogItOptions { RedirectSourcePrefixes = ["/blog"] };

        var context = await InvokeAsync(options, "/login");

        context.Response.Headers.Location.ToString().Should().BeEmpty();
        context.Items.Should().ContainKey("next-was-called");
    }

    [Fact]
    public async Task Middleware_HonoursARedirectInsideTheConfiguredPrefixes()
    {
        var options = new BlogItOptions { RedirectSourcePrefixes = ["/blog"] };

        var context = await InvokeAsync(options, "/blog/old-post");

        context.Response.Headers.Location.ToString().Should().Be("https://evil.example/");
    }

    private static async Task<HttpContext> InvokeAsync(BlogItOptions options, string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = path;

        var middleware = new UrlRedirectMiddleware(next =>
        {
            next.Items["next-was-called"] = true;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context, new AlwaysRedirects(), options);

        return context;
    }

    private static bool TryNormalize(BlogItOptions options, string source, out string error) =>
        RedirectPathValidator.TryNormalize(source, "/destination", options, out _, out _, out error);

    private static void Disable(BlogItOptions options, string rootDocument)
    {
        switch (rootDocument)
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
                throw new ArgumentOutOfRangeException(nameof(rootDocument), rootDocument, null);
        }
    }

    private sealed class AlwaysRedirects : IUrlRedirectService
    {
        public Task<UrlRedirectDto?> FindAsync(string sourcePath) =>
            Task.FromResult<UrlRedirectDto?>(new UrlRedirectDto(
                Guid.NewGuid(),
                sourcePath,
                "https://evil.example/",
                true,
                false,
                DateTime.UtcNow,
                DateTime.UtcNow));

        public Task<IReadOnlyList<UrlRedirectDto>> GetAllAsync() =>
            Task.FromResult<IReadOnlyList<UrlRedirectDto>>([]);

        public Task<UrlRedirectDto?> CreateAsync(string sourcePath, string targetUrl, bool isPermanent) =>
            throw new NotSupportedException();

        public Task<UrlRedirectDto?> UpdateAsync(Guid id, string sourcePath, string targetUrl, bool isPermanent) =>
            throw new NotSupportedException();

        public Task<bool> DeleteAsync(Guid id) => throw new NotSupportedException();

        public Task UpsertAutomaticAsync(string sourcePath, string targetUrl) =>
            throw new NotSupportedException();
    }

    private sealed record InMemoryDatabaseProvider : IBlogItDatabaseProviderRegistration
    {
        public string Name => "fake-db";

        public void RegisterServices(IServiceCollection services) =>
            services.AddDbContextFactory<BlogItDbContext>(options =>
                options.UseInMemoryDatabase($"RedirectPolicy_{Guid.NewGuid():N}"));
    }

    private sealed record NoOpStorageProvider : IBlogItStorageProviderRegistration
    {
        public string Name => "fake-storage";

        public void RegisterServices(IServiceCollection services) =>
            services.AddSingleton<IBlogItMediaStorage, NoOpStorage>();
    }

    private sealed class NoOpStorage : IBlogItMediaStorage
    {
        public Task<string> StoreAsync(
            Stream content,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
