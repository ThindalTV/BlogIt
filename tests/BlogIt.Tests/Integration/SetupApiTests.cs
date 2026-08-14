using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

public class SetupApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task GetStatus_WhenNoUsers_ReturnsIncomplete()
    {
        // Use fresh factory so no users are seeded
        await using var freshFactory = new BlogItSampleFactory();
        var client = freshFactory.CreateClient();
        var response = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        response.Should().NotBeNull();
        response!.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_AfterUserCreated_ReturnsComplete()
    {
        await factory.SeedUserAsync("setup_check_user");
        var client = factory.CreateClient();
        var response = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        response!.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_WhenNoUsers_CreatesUserAndSettings()
    {
        // Use a fresh factory instance for this test to have a clean DB
        await using var freshFactory = new BlogItSampleFactory();
        var client = freshFactory.CreateClient();

        var request = new SetupInitializeRequest(
            Username: "admin",
            DisplayName: "Administrator",
            Password: "AdminPass1!",
            SiteName: "Test Blog",
            SiteUrl: "https://test.com",
            SiteDescription: "A test blog",
            DefaultOgImage: null,
            AiProvider: "openai-compatible",
            AiApiKey: "test-key",
            AiBaseUrl: null,
            AiModel: null,
            AiExportModel: null,
            GoogleAnalyticsMeasurementId: null,
            GoogleAnalyticsPropertyId: null,
            GoogleAnalyticsCredentialsJson: null
        );

        var response = await client.PostAsJsonAsync("/api/setup/initialize", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Status should now be complete
        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        status!.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task Initialize_WhenSetupLockAlreadyClaimed_ReturnsConflictAndDoesNotCreateUser()
    {
        // Regression test for the setup TOCTOU race. This simulates losing the race deterministically
        // rather than firing genuinely concurrent requests: EF Core's InMemory provider (used for
        // tests) has a non-thread-safe dictionary backing store, so true parallel SaveChanges calls
        // against it are undefined behavior (sometimes an exception, sometimes a silent double-insert)
        // — unlike a real relational database, which serializes concurrent writes correctly. Seeding
        // the SetupLock row directly reproduces exactly the state a losing request would see when it
        // reaches SaveChangesAsync: db.Users.AnyAsync() still returns false (the winning request's
        // AppUser insert isn't necessarily durable/visible yet either), but the SetupLock insert
        // fails on the fixed-Id primary key, which is the actual mechanism that closes the race.
        await using var freshFactory = new BlogItSampleFactory();

        using (var scope = freshFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlogIt.Shared.Data.BlogItDbContext>();
            db.SetupLocks.Add(new BlogIt.Shared.Entities.SetupLock());
            await db.SaveChangesAsync();
        }

        var client = freshFactory.CreateClient();
        var request = new SetupInitializeRequest(
            Username: "admin",
            DisplayName: "Administrator",
            Password: "AdminPass1!",
            SiteName: "Test Blog",
            SiteUrl: "https://test.com",
            SiteDescription: "A test blog",
            DefaultOgImage: null,
            AiProvider: "openai-compatible",
            AiApiKey: "test-key",
            AiBaseUrl: null,
            AiModel: null,
            AiExportModel: null,
            GoogleAnalyticsMeasurementId: null,
            GoogleAnalyticsPropertyId: null,
            GoogleAnalyticsCredentialsJson: null);

        var response = await client.PostAsJsonAsync("/api/setup/initialize", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var verifyScope = freshFactory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<BlogIt.Shared.Data.BlogItDbContext>();
        (await verifyDb.Users.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("")]
    public async Task Initialize_WithInvalidSiteUrl_ReturnsValidationProblemAndCreatesNoUser(string invalidSiteUrl)
    {
        await using var freshFactory = new BlogItSampleFactory();
        var client = freshFactory.CreateClient();

        var request = new SetupInitializeRequest(
            Username: "admin",
            DisplayName: "Administrator",
            Password: "AdminPass1!",
            SiteName: "Test Blog",
            SiteUrl: invalidSiteUrl,
            SiteDescription: "A test blog",
            DefaultOgImage: null,
            AiProvider: "openai-compatible",
            AiApiKey: "test-key",
            AiBaseUrl: null,
            AiModel: null,
            AiExportModel: null,
            GoogleAnalyticsMeasurementId: null,
            GoogleAnalyticsPropertyId: null,
            GoogleAnalyticsCredentialsJson: null);

        var response = await client.PostAsJsonAsync("/api/setup/initialize", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = freshFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogIt.Shared.Data.BlogItDbContext>();
        (await db.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Initialize_WithWeakPassword_ReturnsValidationProblemAndCreatesNoUser()
    {
        await using var freshFactory = new BlogItSampleFactory();
        var client = freshFactory.CreateClient();

        var request = new SetupInitializeRequest(
            Username: "admin",
            DisplayName: "Administrator",
            Password: "weak",
            SiteName: "Test Blog",
            SiteUrl: "https://test.com",
            SiteDescription: "A test blog",
            DefaultOgImage: null,
            AiProvider: "openai-compatible",
            AiApiKey: "test-key",
            AiBaseUrl: null,
            AiModel: null,
            AiExportModel: null,
            GoogleAnalyticsMeasurementId: null,
            GoogleAnalyticsPropertyId: null,
            GoogleAnalyticsCredentialsJson: null);

        var response = await client.PostAsJsonAsync("/api/setup/initialize", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = freshFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogIt.Shared.Data.BlogItDbContext>();
        (await db.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Initialize_WhenAlreadySetup_ReturnsConflict()
    {
        await factory.SeedUserAsync("existing_user");
        var client = factory.CreateClient();

        var request = new SetupInitializeRequest(
            "newadmin", "New Admin", "pass", "Site", "https://site.com",
            "Desc", null,
            "openai-compatible", "key", null, null, null, null, null, null);

        var response = await client.PostAsJsonAsync("/api/setup/initialize", request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
