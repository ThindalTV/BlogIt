using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

/// <summary>
/// BlogIt's two analytics halves used to be configured independently: the tag ID drove the
/// client-side snippet, and the property ID plus service-account JSON drove GA4 Data API reporting,
/// with nothing connecting them. Since the Google Tag Manager container is where visitor consent is
/// collected, reporting configured without one describes traffic that was never consented to — so
/// reporting now requires a container ID, on both write paths. See <c>AnalyticsPolicy</c>.
/// <para>
/// Each test gets its own factory: setup can only be run once per site, and the settings cases
/// assert on stored state a shared fixture would let another test move underneath them.
/// </para>
/// </summary>
public class AnalyticsRequiresTagTests
{
    private const string Credentials = @"{""type"":""service_account""}";

    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    /// <summary>
    /// A minimal setup body plus whichever analytics fields the case supplies. Built as text rather
    /// than through <c>SetupInitializeRequest</c> so that "omitted" is genuinely an absent JSON
    /// property, which is the shape an API client that never heard of analytics actually sends.
    /// </summary>
    private static string SetupBody(string? containerId, string? propertyId, string? credentialsJson)
    {
        var analytics = new List<string>();
        if (containerId is not null)
            analytics.Add($"\"googleTagManagerContainerId\": {JsonSerializer.Serialize(containerId)}");
        if (propertyId is not null)
            analytics.Add($"\"googleAnalyticsPropertyId\": {JsonSerializer.Serialize(propertyId)}");
        if (credentialsJson is not null)
            analytics.Add($"\"googleAnalyticsCredentialsJson\": {JsonSerializer.Serialize(credentialsJson)}");

        var tail = analytics.Count == 0 ? "" : ",\n  " + string.Join(",\n  ", analytics);

        return "{\n"
            + "  \"username\": \"owner\",\n"
            + "  \"displayName\": \"Site Owner\",\n"
            + "  \"password\": \"TestPass123\",\n"
            + "  \"siteName\": \"Tagged Site\",\n"
            + "  \"siteUrl\": \"https://example.com\""
            + tail
            + "\n}";
    }

    [Fact]
    public async Task Initialize_WithReportingButNoContainerId_IsRejected()
    {
        await using var factory = new BlogItSampleFactory();

        var response = await factory.CreateClient()
            .PostAsync("/api/setup/initialize", Json(SetupBody(null, "123456789", Credentials)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Container ID");
    }

    [Fact]
    public async Task Initialize_WithCredentialsAloneAndNoContainerId_IsRejected()
    {
        // The credentials are the half that matters most: they are a real secret, stored against a
        // property the site has no tag feeding.
        await using var factory = new BlogItSampleFactory();

        var response = await factory.CreateClient()
            .PostAsync("/api/setup/initialize", Json(SetupBody(null, null, Credentials)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Initialize_WithAContainerIdAndNoReporting_Succeeds()
    {
        // Tracking on, reporting off: the ordinary case for a site that has not installed
        // BlogIt.GoogleAnalytics. The rule is one-directional and must not break it.
        await using var factory = new BlogItSampleFactory();

        var response = await factory.CreateClient()
            .PostAsync("/api/setup/initialize", Json(SetupBody("GTM-ABC123", null, null)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Initialize_WithAContainerIdAndReporting_Succeeds()
    {
        await using var factory = new BlogItSampleFactory();

        var response = await factory.CreateClient()
            .PostAsync("/api/setup/initialize", Json(SetupBody("GTM-ABC123", "123456789", Credentials)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        (await settings.GetAsync(SettingKeys.GoogleAnalyticsPropertyId)).Should().Be("123456789");
    }

    [Fact]
    public async Task Initialize_WithAGa4MeasurementIdInsteadOfAContainerId_IsRejected()
    {
        // The mistake the shape check exists for: G-… is a GA4 measurement ID, and the container
        // loader this value is interpolated into does not serve one. Accepted, it would render a
        // tag that silently never fires.
        await using var factory = new BlogItSampleFactory();

        var response = await factory.CreateClient()
            .PostAsync("/api/setup/initialize", Json(SetupBody("G-ABC123", null, null)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_WithAMalformedContainerId_IsRejected()
    {
        await using var factory = new BlogItSampleFactory();
        var userId = await factory.SeedUserAsync($"gtm_malformed_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.PutAsJsonAsync("/api/settings",
            new SiteSettingsUpdateRequest(GoogleTagManagerContainerId: "G-ABC123"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_WithReportingAndNoStoredContainerId_IsRejected()
    {
        await using var factory = new BlogItSampleFactory();
        var userId = await factory.SeedUserAsync($"ga_no_tag_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.PutAsJsonAsync("/api/settings",
            new SiteSettingsUpdateRequest(GoogleAnalyticsPropertyId: "123456789"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateSettings_WithTheContainerIdAndReportingTogether_Succeeds()
    {
        await using var factory = new BlogItSampleFactory();
        var userId = await factory.SeedUserAsync($"ga_together_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.PutAsJsonAsync("/api/settings", new SiteSettingsUpdateRequest(
            GoogleTagManagerContainerId: "GTM-ABC123",
            GoogleAnalyticsPropertyId: "123456789",
            GoogleAnalyticsCredentialsJson: Credentials));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateSettings_AddingReportingToAnAlreadyStoredContainerId_Succeeds()
    {
        // The check runs on the effective settings, not the body — a partial update carrying only a
        // property ID has to be judged against the container ID already stored, or a client that
        // saves one section at a time would be locked out of reporting entirely.
        await using var factory = new BlogItSampleFactory();
        var userId = await factory.SeedUserAsync($"ga_stored_tag_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        (await client.PutAsJsonAsync("/api/settings",
                new SiteSettingsUpdateRequest(GoogleTagManagerContainerId: "GTM-ABC123")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync("/api/settings",
            new SiteSettingsUpdateRequest(GoogleAnalyticsPropertyId: "123456789"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateSettings_ClearingTheContainerIdWhileReportingRemains_IsRejected()
    {
        // Removing the tag has to remove reporting with it. Refusing beats silently deleting the
        // stored credentials, and the admin screen clears all three together so nobody meets this.
        await using var factory = new BlogItSampleFactory();
        var userId = await factory.SeedUserAsync($"ga_clear_tag_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        (await client.PutAsJsonAsync("/api/settings", new SiteSettingsUpdateRequest(
                GoogleTagManagerContainerId: "GTM-ABC123",
                GoogleAnalyticsPropertyId: "123456789")))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync("/api/settings",
            new SiteSettingsUpdateRequest(GoogleTagManagerContainerId: ""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        (await settings.GetAsync(SettingKeys.GoogleTagManagerContainerId)).Should().Be("GTM-ABC123");
    }

    [Fact]
    public async Task UpdateSettings_ClearingTheContainerIdAndReportingTogether_Succeeds()
    {
        await using var factory = new BlogItSampleFactory();
        var userId = await factory.SeedUserAsync($"ga_clear_all_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        (await client.PutAsJsonAsync("/api/settings", new SiteSettingsUpdateRequest(
                GoogleTagManagerContainerId: "GTM-ABC123",
                GoogleAnalyticsPropertyId: "123456789",
                GoogleAnalyticsCredentialsJson: Credentials)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await client.PutAsJsonAsync("/api/settings", new SiteSettingsUpdateRequest(
            GoogleTagManagerContainerId: "",
            GoogleAnalyticsPropertyId: "",
            GoogleAnalyticsCredentialsJson: ""));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = factory.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        (await settings.GetAsync(SettingKeys.GoogleAnalyticsCredentialsJson)).Should().BeNullOrEmpty();
    }
}
