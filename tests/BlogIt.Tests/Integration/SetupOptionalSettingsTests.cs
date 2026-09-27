using System.Net;
using System.Net.Http.Json;
using System.Text;
using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

/// <summary>
/// A site set up without AI or analytics is an ordinary site, and first-run setup has to accept one.
/// It did not: <c>SiteSetting.Value</c> was <c>NOT NULL</c>, and <c>SetupApi</c> assigned
/// <c>SiteDescription</c>, <c>AiProvider</c> and <c>AiApiKey</c> straight from the request while
/// guarding only the other optional fields — so omitting any of those three died in
/// <c>SaveChanges</c> as an unhandled <c>DbUpdateException</c>, surfaced as a 500. The bundled wizard
/// binds every input to <c>""</c> and so always sent something, which is why only API clients hit it.
/// <para>
/// Null is now the stored representation of "not set", so these requests succeed and the unset
/// settings read back as null rather than as an empty string.
/// </para>
/// </summary>
public class SetupOptionalSettingsTests
{
    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    /// <summary>The smallest legal setup body: everything optional left out.</summary>
    private const string MinimalSetup = """
        {
          "username": "owner",
          "displayName": "Site Owner",
          "password": "TestPass123",
          "siteName": "Minimal Site",
          "siteUrl": "https://example.com"
        }
        """;

    [Fact]
    public async Task Initialize_WithEveryOptionalFieldOmitted_Succeeds()
    {
        await using var factory = new BlogItSampleFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/api/setup/initialize", Json(MinimalSetup));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Initialize_RecordsOmittedOptionalSettingsAsNull_NotEmptyString()
    {
        await using var factory = new BlogItSampleFactory();
        var client = factory.CreateClient();

        (await client.PostAsync("/api/setup/initialize", Json(MinimalSetup)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        foreach (var key in new[]
                 {
                     SettingKeys.AiApiKey,
                     SettingKeys.AiBaseUrl,
                     SettingKeys.AiModel,
                     SettingKeys.AiExportModel,
                     SettingKeys.AiProvider,
                     SettingKeys.SiteDescription,
                     SettingKeys.DefaultOgImage,
                     SettingKeys.GoogleTagManagerContainerId,
                     SettingKeys.GoogleAnalyticsPropertyId,
                     SettingKeys.GoogleAnalyticsCredentialsJson,
                 })
        {
            (await settings.GetAsync(key)).Should().BeNull($"{key} was not set during setup");
        }
    }

    [Fact]
    public async Task Initialize_StillStoresTheSettingsThatWereSupplied()
    {
        await using var factory = new BlogItSampleFactory();
        var client = factory.CreateClient();

        (await client.PostAsync("/api/setup/initialize", Json(MinimalSetup)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        (await settings.GetAsync(SettingKeys.SiteName)).Should().Be("Minimal Site");
        (await settings.GetAsync(SettingKeys.SiteUrl)).Should().Be("https://example.com");
        (await settings.GetAsync(SettingKeys.JwtSecret)).Should().NotBeNullOrWhiteSpace();

        // Completion is asserted through the status endpoint rather than the retired
        // SettingKeys.SetupComplete row, which setup no longer writes: whether a site is claimed is
        // decided by whether a user exists, and this is the contract clients actually read.
        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        status!.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task ASettingCanBeNull_EmptyOrSet_AndTheThreeStayDistinct()
    {
        // The same convention optional post and page text follows: null is "not set", an empty
        // string is "set to nothing". Storing both as "" would make an unconfigured AI key
        // indistinguishable from one the operator deliberately cleared.
        await using var factory = new BlogItSampleFactory();
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        await settings.SetManyAsync(new Dictionary<string, string?>
        {
            ["test.notSet"] = null,
            ["test.empty"] = "",
            ["test.set"] = "value",
        });

        (await settings.GetAsync("test.notSet")).Should().BeNull();
        (await settings.GetAsync("test.empty")).Should().Be("");
        (await settings.GetAsync("test.set")).Should().Be("value");

        var all = await settings.GetAllAsync();
        all["test.notSet"].Should().BeNull();
        all["test.empty"].Should().Be("");
    }

    [Fact]
    public async Task ASetSettingCanBeReturnedToNotSet()
    {
        await using var factory = new BlogItSampleFactory();
        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();

        await settings.SetAsync("test.roundTrip", "configured");
        (await settings.GetAsync("test.roundTrip")).Should().Be("configured");

        await settings.SetAsync("test.roundTrip", null);

        (await settings.GetAsync("test.roundTrip")).Should().BeNull();
    }
}
