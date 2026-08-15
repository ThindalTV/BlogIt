using BlogIt.Shared.DTOs;
using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

public class SiteSettingsValidatorTests
{
    [Fact]
    public void Validate_EmptyRequest_IsValid()
    {
        // Every field null means "change nothing" — a legitimate no-op, not an error.
        SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest()).Should().BeEmpty();
    }

    [Fact]
    public void Validate_FullyPopulatedValidRequest_IsValid()
    {
        var request = new SiteSettingsUpdateRequest(
            SiteName: "My Blog",
            SiteUrl: "https://example.com",
            SiteDescription: "A blog",
            DefaultOgImage: "https://example.com/og.png",
            AiProvider: "openai-compatible",
            AiBaseUrl: "https://api.openai.com/v1",
            AiModel: "gpt-4o",
            AiExportModel: "gpt-4o",
            AiApiKey: "sk-real-key",
            GoogleAnalyticsMeasurementId: "G-XXXXXXXXXX",
            GoogleAnalyticsPropertyId: "123456789",
            GoogleAnalyticsCredentialsJson: "{}",
            JwtExpiryMinutes: 1440);

        SiteSettingsValidator.Validate(request).Should().BeEmpty();
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RejectsNonAbsoluteHttpSiteUrl(string siteUrl)
    {
        var errors = SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest(SiteUrl: siteUrl));

        errors.Should().ContainKey("siteUrl");
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/blog")]
    public void Validate_AcceptsAbsoluteHttpSiteUrl(string siteUrl)
    {
        SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest(SiteUrl: siteUrl))
            .Should().NotContainKey("siteUrl");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(10081)]
    [InlineData(int.MaxValue)]
    public void Validate_RejectsJwtExpiryOutsideBounds(int minutes)
    {
        // int.MaxValue is the one that matters most: DateTime.AddMinutes overflows on it, and
        // anything merely large mints a token nothing can revoke before it expires.
        var errors = SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest(JwtExpiryMinutes: minutes));

        errors.Should().ContainKey("jwtExpiryMinutes");
    }

    [Theory]
    [InlineData(SiteSettingsValidator.MinJwtExpiryMinutes)]
    [InlineData(60)]
    [InlineData(1440)]
    [InlineData(SiteSettingsValidator.MaxJwtExpiryMinutes)]
    public void Validate_AcceptsJwtExpiryWithinBounds(int minutes)
    {
        SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest(JwtExpiryMinutes: minutes))
            .Should().NotContainKey("jwtExpiryMinutes");
    }

    [Theory]
    [InlineData("bring-your-own-model")]
    [InlineData("")]
    public void Validate_RejectsUnknownAiProvider(string provider)
    {
        var errors = SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest(AiProvider: provider));

        errors.Should().ContainKey("aiProvider");
    }

    [Theory]
    [InlineData("openai-compatible")]
    [InlineData("github-copilot")]
    [InlineData("GitHub-Copilot")]
    public void Validate_AcceptsKnownAiProviderRegardlessOfCasing(string provider)
    {
        SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest(AiProvider: provider))
            .Should().NotContainKey("aiProvider");
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("file:///etc/passwd")]
    public void Validate_RejectsNonHttpAiBaseUrl(string baseUrl)
    {
        // The configured API key is sent to whatever this resolves to, so it has to be a real
        // absolute http(s) endpoint before anything is persisted.
        var errors = SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest(AiBaseUrl: baseUrl));

        errors.Should().ContainKey("aiBaseUrl");
    }

    [Fact]
    public void Validate_AllowsBlankAiBaseUrlToClearIt()
    {
        // Blank is meaningful here, unlike SiteUrl: it falls back to the provider default.
        SiteSettingsValidator.Validate(new SiteSettingsUpdateRequest(AiBaseUrl: ""))
            .Should().NotContainKey("aiBaseUrl");
    }

    [Fact]
    public void Validate_ReportsEveryInvalidFieldAtOnce()
    {
        var request = new SiteSettingsUpdateRequest(
            SiteUrl: "nope",
            AiProvider: "unknown",
            AiBaseUrl: "also-nope",
            JwtExpiryMinutes: 0);

        var errors = SiteSettingsValidator.Validate(request);

        errors.Keys.Should().BeEquivalentTo("siteUrl", "aiProvider", "aiBaseUrl", "jwtExpiryMinutes");
    }
}
