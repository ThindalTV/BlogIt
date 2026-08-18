using System.ComponentModel.DataAnnotations;

namespace BlogIt.Shared.DTOs;

public record SetupStatusResponse(bool IsComplete);

/// <remarks>
/// Only the account fields carry ceilings, because only they have a constant in this assembly. The
/// site fields below are validated by the engine's <c>SiteSettingsValidator</c> and its
/// <c>UrlValidator</c>; their rules are not restated here — see <see cref="CreateBlogPostRequest"/>.
/// </remarks>
public record SetupInitializeRequest(
    [property: Required][property: StringLength(ContentLimits.UsernameLength)] string Username,
    [property: Required][property: StringLength(ContentLimits.DisplayNameLength)] string DisplayName,
    [property: Required] string Password,
    string SiteName,
    string SiteUrl,
    string SiteDescription,
    string? DefaultOgImage,
    string AiProvider,
    string AiApiKey,
    string? AiBaseUrl,
    string? AiModel,
    string? AiExportModel,
    string? GoogleAnalyticsMeasurementId,
    string? GoogleAnalyticsPropertyId,
    string? GoogleAnalyticsCredentialsJson
);
