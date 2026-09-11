using BlogIt.Shared.DTOs;

namespace BlogIt;

/// <summary>
/// The administrator account and site settings to claim an unclaimed BlogIt site with, for
/// <c>InitializeBlogItAsync</c>.
/// </summary>
/// <remarks>
/// A separate type from <see cref="SetupInitializeRequest"/>, which is the wire shape: that is a
/// fifteen-member positional record, and calling it from C# means counting commas past six optional
/// AI fields to reach the analytics ones. Named init properties make a provisioning call readable
/// and let it mention only what it sets.
/// </remarks>
public sealed record BlogItSetupRequest
{
    /// <summary>Username for the first administrator account.</summary>
    public required string Username { get; init; }

    /// <summary>Display name shown as the author on posts.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Password for the first administrator account. Must satisfy
    /// <see cref="Shared.Helpers.PasswordPolicy"/>.
    /// </summary>
    public required string Password { get; init; }

    /// <summary>The site's name, used as the feed title and the default page title suffix.</summary>
    public required string SiteName { get; init; }

    /// <summary>
    /// The site's absolute public URL. Must be <c>http://</c> or <c>https://</c>; it is what feeds,
    /// the sitemap and canonical URLs are built from.
    /// </summary>
    public required string SiteUrl { get; init; }

    /// <summary>The site's description, used in feed metadata.</summary>
    public string? SiteDescription { get; init; }

    /// <summary>Fallback Open Graph image URL for pages that specify none.</summary>
    public string? DefaultOgImage { get; init; }

    /// <summary>
    /// AI provider name, or null to set the site up without AI. Optional here even though the setup
    /// wizard asks for it: a provisioned site is expected to configure AI later, or never.
    /// </summary>
    public string? AiProvider { get; init; }

    /// <inheritdoc cref="AiProvider"/>
    public string? AiApiKey { get; init; }

    /// <inheritdoc cref="AiProvider"/>
    public string? AiBaseUrl { get; init; }

    /// <inheritdoc cref="AiProvider"/>
    public string? AiModel { get; init; }

    /// <inheritdoc cref="AiProvider"/>
    public string? AiExportModel { get; init; }

    /// <summary>
    /// Google Tag Manager container ID, or null for no analytics. Analytics reporting cannot be
    /// configured without one — see <c>AnalyticsPolicy</c>.
    /// </summary>
    public string? GoogleTagManagerContainerId { get; init; }

    /// <inheritdoc cref="GoogleTagManagerContainerId"/>
    public string? GoogleAnalyticsPropertyId { get; init; }

    /// <inheritdoc cref="GoogleTagManagerContainerId"/>
    public string? GoogleAnalyticsCredentialsJson { get; init; }

    internal SetupInitializeRequest ToWireRequest() => new(
        Username: Username,
        DisplayName: DisplayName,
        Password: Password,
        SiteName: SiteName,
        SiteUrl: SiteUrl,
        SiteDescription: SiteDescription ?? string.Empty,
        DefaultOgImage: DefaultOgImage,
        AiProvider: AiProvider ?? string.Empty,
        AiApiKey: AiApiKey ?? string.Empty,
        AiBaseUrl: AiBaseUrl,
        AiModel: AiModel,
        AiExportModel: AiExportModel,
        GoogleTagManagerContainerId: GoogleTagManagerContainerId,
        GoogleAnalyticsPropertyId: GoogleAnalyticsPropertyId,
        GoogleAnalyticsCredentialsJson: GoogleAnalyticsCredentialsJson);
}
