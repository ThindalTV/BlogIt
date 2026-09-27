using BlogIt;
using BlogIt.Shared.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Covers the startup checks that turn BlogIt's integration rules from prose into failures.
/// </summary>
/// <remarks>
/// Every rule here used to live only in documentation, and every way of breaking one failed
/// silently or late: a missing <c>UseBlogIt</c> leaves an application that starts and serves the
/// admin portal but never runs a redirect, and a duplicated root document throws
/// <c>AmbiguousMatchException</c> on the first request rather than at startup.
/// </remarks>
public sealed class StartupValidationTests
{
    [Fact]
    public async Task AnOrdinaryHost_StartsWithoutComplaint()
    {
        // The assertion that matters most in this file. A validator that fires on a correct
        // application is worse than no validator, so the well-formed case is asserted first.
        var app = Build(host =>
        {
            host.UseBlogIt();
            host.MapBlogIt();
        });

        var start = async () => await app.StartAsync();

        await start.Should().NotThrowAsync();
        await app.DisposeAsync();
    }

    [Fact]
    public async Task ForgettingUseBlogIt_FailsAtStartupNamingTheCall()
    {
        // The silent one: without UseBlogIt the redirect middleware is missing, but WebApplication
        // still adds authentication and authorization on its own, so the site starts, the admin
        // works, and the only symptom is that no redirect ever fires.
        var app = Build(host => host.MapBlogIt());

        var start = async () => await app.StartAsync();

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*UseBlogIt was never called*")
            .WithMessage("*redirects will never run*");

        await app.DisposeAsync();
    }

    [Fact]
    public async Task ForgettingMapBlogIt_FailsAtStartupNamingTheCall()
    {
        var app = Build(host => host.UseBlogIt());

        var start = async () => await app.StartAsync();

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*MapBlogIt was never called*");

        await app.DisposeAsync();
    }

    [Fact]
    public async Task MappingARootDocumentTheHostAlsoOwns_FailsAtStartupNamingTheOption()
    {
        var app = Build(host =>
        {
            host.UseBlogIt();
            host.MapBlogIt();
            host.MapGet("/sitemap.xml", () => "the host's own sitemap");
        });

        var start = async () => await app.StartAsync();

        // Instead of an AmbiguousMatchException on the first crawler request, with an exception
        // message that names neither BlogIt nor the switch that resolves it.
        (await start.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*/sitemap.xml*")
            .WithMessage("*ServeSitemap*");

        await app.DisposeAsync();
    }

    [Fact]
    public async Task TurningTheRootDocumentOff_LeavesTheRouteToTheHost()
    {
        // The documented fix for the previous test has to actually work, or the error message is
        // sending people somewhere that fails differently.
        var app = Build(
            host =>
            {
                host.UseBlogIt();
                host.MapBlogIt();
                host.MapGet("/sitemap.xml", () => "the host's own sitemap");
            },
            options => options.ServeSitemap = false);

        await app.StartAsync();

        var body = await app.GetTestClient().GetStringAsync("/sitemap.xml");
        body.Should().Be("the host's own sitemap");

        await app.DisposeAsync();
    }

    [Fact]
    public async Task AHostRouteThatMerelyLooksSimilar_IsNotReportedAsAConflict()
    {
        var app = Build(host =>
        {
            host.UseBlogIt();
            host.MapBlogIt();
            host.MapGet("/sitemap", () => "not the same route");
            host.MapGet("/blog/rss.xml", () => "nor this one");
        });

        var start = async () => await app.StartAsync();

        await start.Should().NotThrowAsync();
        await app.DisposeAsync();
    }

    private static WebApplication Build(
        Action<WebApplication> configure,
        Action<BlogItOptions>? configureOptions = null)
    {
        var storageRoot = Path.Combine(AppContext.BaseDirectory, $"startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(
                new InMemoryDatabaseProvider($"Startup_{Guid.NewGuid():N}"));
            options.UseFileSystemStorage(storage => storage.RootPath = storageRoot);
            configureOptions?.Invoke(options);
        });

        var app = builder.Build();
        configure(app);
        return app;
    }

    private sealed class InMemoryDatabaseProvider(string databaseName)
        : IBlogItDatabaseProviderRegistration
    {
        public string Name => "test-in-memory";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddDbContextFactory<BlogItDbContext>(
                options => options.UseInMemoryDatabase(databaseName));
            services.AddSingleton<IBlogItMigrator, EnsureCreatedMigrator>();
        }
    }

    private sealed class EnsureCreatedMigrator(
        IDbContextFactory<BlogItDbContext> factory) : IBlogItMigrator
    {
        public async Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
