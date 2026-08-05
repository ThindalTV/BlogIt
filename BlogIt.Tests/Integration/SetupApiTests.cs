using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

public class SetupApiTests(BlogItWebFactory factory) : IClassFixture<BlogItWebFactory>
{
    [Fact]
    public async Task GetStatus_WhenNoUsers_ReturnsIncomplete()
    {
        // Use fresh factory so no users are seeded
        await using var freshFactory = new BlogItWebFactory();
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
        await using var freshFactory = new BlogItWebFactory();
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
