using System.Net;
using System.Net.Http.Json;
using BlogIt;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Data;
using BlogIt.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Covers <c>InitializeBlogItAsync</c>, the way to claim a site without a human at a browser.
/// </summary>
/// <remarks>
/// Until this existed there was no supported way to create the first user at all: the setup wizard
/// was the only path, and the users endpoint requires an administrator to already exist. That made
/// CI, end-to-end tests and container provisioning impossible to automate.
/// </remarks>
public sealed class ProgrammaticSetupTests
{
    private static readonly BlogItSetupRequest Setup = new()
    {
        Username = "owner",
        DisplayName = "Site Owner",
        Password = "Str0ng-Password!",
        SiteName = "Provisioned",
        SiteUrl = "https://example.com"
    };

    [Fact]
    public async Task Initialize_ClaimsTheSiteAndTheSeededAccountCanLogIn()
    {
        await using var app = Build();
        await app.MigrateBlogItAsync();

        (await app.InitializeBlogItAsync(Setup)).Should().BeTrue();
        await app.StartAsync();

        // The regression test for the split-transaction bug. Setup used to commit the user and the
        // setup lock in one transaction and then write JwtSecret in a second one; a crash in
        // between left a site with an administrator nobody could authenticate as and no way to
        // re-run setup, because a user now existed. Logging in proves both halves committed.
        var response = await app.GetTestClient().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest("owner", "Str0ng-Password!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<LoginResponse>())!
            .Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Initialize_IsIdempotentSoAContainerCanCallItOnEveryStart()
    {
        await using var app = Build();
        await app.MigrateBlogItAsync();

        (await app.InitializeBlogItAsync(Setup)).Should().BeTrue();

        // False rather than an exception: the natural caller runs on every start, so being called
        // again is the expected case, not an error to be caught.
        (await app.InitializeBlogItAsync(Setup)).Should().BeFalse();
    }

    [Fact]
    public async Task Initialize_ThrowsOnAnInvalidRequestBecauseThatIsACodeDefect()
    {
        await using var app = Build();
        await app.MigrateBlogItAsync();

        var initialize = async () => await app.InitializeBlogItAsync(new BlogItSetupRequest
        {
            Username = "owner",
            DisplayName = "Site Owner",
            Password = "short",
            SiteName = "Provisioned",
            SiteUrl = "not-a-url"
        });

        // Unlike "already claimed", an invalid request means the calling code is wrong rather than
        // that the site is in a particular state, so it is raised rather than reported.
        (await initialize.Should().ThrowAsync<ArgumentException>())
            .WithMessage("*siteUrl*")
            .WithMessage("*password*");
    }

    [Fact]
    public async Task Initialize_AppliesTheSameValidationAsTheSetupEndpoint()
    {
        await using var app = Build();
        await app.MigrateBlogItAsync();
        await app.StartAsync();

        var overHttp = await app.GetTestClient().PostAsJsonAsync(
            "/api/setup/initialize",
            new SetupInitializeRequest(
                Username: "owner",
                DisplayName: "Site Owner",
                Password: "short",
                SiteName: "Provisioned",
                SiteUrl: "not-a-url",
                SiteDescription: string.Empty,
                DefaultOgImage: null,
                AiProvider: string.Empty,
                AiApiKey: string.Empty,
                AiBaseUrl: null,
                AiModel: null,
                AiExportModel: null,
                GoogleTagManagerContainerId: null,
                GoogleAnalyticsPropertyId: null,
                GoogleAnalyticsCredentialsJson: null));

        // Both entry points run one implementation, so the anonymous HTTP route cannot become a way
        // around a rule the programmatic path enforces, or the other way round.
        overHttp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Initialize_ClosesTheSetupEndpointWithoutNeedingAnOptionToDisableIt()
    {
        await using var app = Build();
        await app.MigrateBlogItAsync();
        await app.InitializeBlogItAsync(Setup);
        await app.StartAsync();

        var client = app.GetTestClient();

        // A host provisioning from configuration gets what an "EnableSetupEndpoint" switch would
        // have bought it for free: initialize before Run, and the route is closed before the first
        // request is ever served.
        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        status!.IsComplete.Should().BeTrue();

        var retry = await client.PostAsJsonAsync(
            "/api/setup/initialize",
            new SetupInitializeRequest(
                Username: "intruder",
                DisplayName: "Intruder",
                Password: "Str0ng-Password!",
                SiteName: "Hijacked",
                SiteUrl: "https://evil.example",
                SiteDescription: string.Empty,
                DefaultOgImage: null,
                AiProvider: string.Empty,
                AiApiKey: string.Empty,
                AiBaseUrl: null,
                AiModel: null,
                AiExportModel: null,
                GoogleTagManagerContainerId: null,
                GoogleAnalyticsPropertyId: null,
                GoogleAnalyticsCredentialsJson: null));

        retry.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Initialize_StoresTheSettingsItWasGiven()
    {
        await using var app = Build();
        await app.MigrateBlogItAsync();
        await app.InitializeBlogItAsync(Setup with { SiteDescription = "A provisioned blog" });

        using var scope = app.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        (await settings.GetAsync(SettingKeys.SiteName)).Should().Be("Provisioned");
        (await settings.GetAsync(SettingKeys.SiteDescription)).Should().Be("A provisioned blog");
        (await settings.GetAsync(SettingKeys.JwtSecret)).Should().NotBeNullOrWhiteSpace();
        // Omitted AI settings are stored as null, which is how "not set" is represented.
        (await settings.GetAsync(SettingKeys.AiProvider)).Should().BeNull();
    }

    private static WebApplication Build()
    {
        var storageRoot = Path.Combine(AppContext.BaseDirectory, $"setup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new InMemoryDatabaseProvider($"Setup_{Guid.NewGuid():N}"));
            options.UseFileSystemStorage(storage => storage.RootPath = storageRoot);
        });

        var app = builder.Build();
        app.UseBlogIt();
        app.MapBlogIt();
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
