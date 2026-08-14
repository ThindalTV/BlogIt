using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

public class SettingsApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task GetSettings_RequiresAuth()
    {
        var response = await factory.CreateClient().GetAsync("/api/settings");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSettings_WithAuth_RedactsSensitiveKeys()
    {
        var userId = await factory.SeedUserAsync("settings_reader");
        var client = factory.CreateClient().WithAuth(userId);

        var settings = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/settings");
        settings.Should().NotBeNull();

        // JwtSecret should be redacted
        if (settings!.TryGetValue(BlogIt.Shared.SettingKeys.JwtSecret, out var val))
            val.Should().Be("***");
    }

    [Fact]
    public async Task UpdateSettings_WithAuth_PersistsValues()
    {
        var userId = await factory.SeedUserAsync("settings_writer");
        var client = factory.CreateClient().WithAuth(userId);

        var update = new Dictionary<string, string>
        {
            [BlogIt.Shared.SettingKeys.SiteName] = "Updated Blog Name",
            [BlogIt.Shared.SettingKeys.SiteDescription] = "Updated description"
        };

        var putResponse = await client.PutAsJsonAsync("/api/settings", update);
        putResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var settings = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/settings");
        settings![BlogIt.Shared.SettingKeys.SiteName].Should().Be("Updated Blog Name");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    public async Task UpdateSettings_WithInvalidSiteUrl_ReturnsBadRequest(string invalidSiteUrl)
    {
        var userId = await factory.SeedUserAsync($"settings_bad_url_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var update = new Dictionary<string, string>
        {
            [BlogIt.Shared.SettingKeys.SiteUrl] = invalidSiteUrl
        };

        var putResponse = await client.PutAsJsonAsync("/api/settings", update);
        putResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_WithValidSiteUrl_Persists()
    {
        var userId = await factory.SeedUserAsync($"settings_good_url_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var update = new Dictionary<string, string>
        {
            [BlogIt.Shared.SettingKeys.SiteUrl] = "https://updated.example.com"
        };

        var putResponse = await client.PutAsJsonAsync("/api/settings", update);
        putResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var settings = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/settings");
        settings![BlogIt.Shared.SettingKeys.SiteUrl].Should().Be("https://updated.example.com");
    }
}
