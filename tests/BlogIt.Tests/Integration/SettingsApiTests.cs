using System.Net;
using System.Net.Http.Json;
using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task UpdateSettings_CannotWriteJwtSecret()
    {
        // The lockout this closes: a JwtSecret shorter than the 128 bits HS256 needs makes every
        // subsequent login throw, with no screen in the admin that can put it back. The typed
        // body has no JwtSecret property, so the key is unreachable rather than merely guarded.
        var userId = await factory.SeedUserAsync($"settings_jwt_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var originalSecret = await ReadStoredSettingAsync(SettingKeys.JwtSecret);

        var putResponse = await client.PutAsJsonAsync("/api/settings", new Dictionary<string, string>
        {
            [SettingKeys.JwtSecret] = "short",
            [SettingKeys.SiteName] = "Still Saved"
        });

        putResponse.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await ReadStoredSettingAsync(SettingKeys.JwtSecret)).Should().Be(originalSecret);
    }

    [Fact]
    public async Task UpdateSettings_CannotWriteAzureStorageConfiguration()
    {
        var userId = await factory.SeedUserAsync($"settings_azure_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        await client.PutAsJsonAsync("/api/settings", new Dictionary<string, string>
        {
            [SettingKeys.AzureStorageConnectionString] = "UseDevelopmentStorage=true",
            [SettingKeys.AzureStorageContainer] = "hijacked"
        });

        (await ReadStoredSettingAsync(SettingKeys.AzureStorageConnectionString)).Should().BeNull();
        (await ReadStoredSettingAsync(SettingKeys.AzureStorageContainer)).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(525600)]
    public async Task UpdateSettings_WithJwtExpiryOutOfRange_ReturnsBadRequest(int minutes)
    {
        var userId = await factory.SeedUserAsync($"settings_expiry_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.PutAsJsonAsync(
            "/api/settings",
            new SiteSettingsUpdateRequest(JwtExpiryMinutes: minutes));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_WithRedactedSecretPlaceholder_KeepsTheRealSecret()
    {
        // GET redacts secrets to "***"; a client that round-trips the fetched object must not
        // overwrite the real credential with the mask.
        var userId = await factory.SeedUserAsync($"settings_redact_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        await client.PutAsJsonAsync("/api/settings", new SiteSettingsUpdateRequest(AiApiKey: "sk-the-real-key"));
        await client.PutAsJsonAsync(
            "/api/settings",
            new SiteSettingsUpdateRequest(AiApiKey: SettingsRedaction.Placeholder, SiteName: "Changed"));

        (await ReadStoredSettingAsync(SettingKeys.AiApiKey)).Should().Be("sk-the-real-key");
        (await ReadStoredSettingAsync(SettingKeys.SiteName)).Should().Be("Changed");
    }

    [Fact]
    public async Task UpdateSettings_OmittedFieldsAreLeftUnchanged()
    {
        var userId = await factory.SeedUserAsync($"settings_partial_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        await client.PutAsJsonAsync(
            "/api/settings",
            new SiteSettingsUpdateRequest(SiteName: "Keep Me", SiteDescription: "Keep me too"));
        await client.PutAsJsonAsync("/api/settings", new SiteSettingsUpdateRequest(SiteDescription: "Replaced"));

        (await ReadStoredSettingAsync(SettingKeys.SiteName)).Should().Be("Keep Me");
        (await ReadStoredSettingAsync(SettingKeys.SiteDescription)).Should().Be("Replaced");
    }

    [Fact]
    public async Task UpdateSettings_WithUnknownAiProvider_ReturnsBadRequest()
    {
        var userId = await factory.SeedUserAsync($"settings_provider_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.PutAsJsonAsync(
            "/api/settings",
            new SiteSettingsUpdateRequest(AiProvider: "not-a-provider"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RotateJwtSecret_ReplacesTheSecretWithAServerGeneratedValue()
    {
        var userId = await factory.SeedUserAsync($"settings_rotate_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var before = await ReadStoredSettingAsync(SettingKeys.JwtSecret);

        var response = await client.PostAsync("/api/settings/jwt-secret/rotate", null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        var after = await ReadStoredSettingAsync(SettingKeys.JwtSecret);
        after.Should().NotBeNullOrWhiteSpace();
        after.Should().NotBe(before);

        // Must clear the 128 bits HS256 requires by a wide margin, and never be echoed back.
        Convert.FromBase64String(after!).Length.Should().BeGreaterThanOrEqualTo(32);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(after);
    }

    [Fact]
    public async Task RotateJwtSecret_RequiresAuth()
    {
        var response = await factory.CreateClient().PostAsync("/api/settings/jwt-secret/rotate", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string?> ReadStoredSettingAsync(string key)
    {
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        return await settings.GetAsync(key);
    }
}
