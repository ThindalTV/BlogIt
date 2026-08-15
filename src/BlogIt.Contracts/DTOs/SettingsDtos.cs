namespace BlogIt.Shared.DTOs;

/// <summary>
/// The complete set of site settings an admin client is allowed to write.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately replaced a <c>Dictionary&lt;string, string&gt;</c> body. Two keys can break
/// the installation in ways no in-app screen can repair, and neither is reachable through this
/// shape: <c>JwtSecret</c> has no property here at all — rotate it through
/// <c>POST /settings/jwt-secret/rotate</c>, which generates the value server-side — and the Azure
/// storage keys are application configuration, not site settings, so they are absent too rather
/// than blocked by a runtime check that a new key would slip past.
/// </para>
/// <para>
/// Every property is nullable and null means "leave this setting as it is", so a client may send
/// a partial update. To clear a setting, send an empty string for it.
/// </para>
/// </remarks>
public record SiteSettingsUpdateRequest(
    string? SiteName = null,
    string? SiteUrl = null,
    string? SiteDescription = null,
    string? DefaultOgImage = null,
    string? AiProvider = null,
    string? AiBaseUrl = null,
    string? AiModel = null,
    string? AiExportModel = null,
    string? AiApiKey = null,
    string? GoogleAnalyticsMeasurementId = null,
    string? GoogleAnalyticsPropertyId = null,
    string? GoogleAnalyticsCredentialsJson = null,
    int? JwtExpiryMinutes = null);

/// <summary>How <c>GET /settings</c> masks secrets, and what a write does with that mask.</summary>
public static class SettingsRedaction
{
    /// <summary>
    /// Stands in for any secret's value in a <c>GET /settings</c> response. A write carrying this
    /// exact value for a secret field is treated as "unchanged" rather than persisted, so a
    /// client that round-trips a fetched settings object cannot overwrite a real credential with
    /// the mask. Clients should still send null for secrets the user did not retype.
    /// </summary>
    public const string Placeholder = "***";
}
