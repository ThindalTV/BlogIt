namespace BlogIt.Shared.DTOs;

public record SetupStatusResponse(bool IsComplete);

public record SetupInitializeRequest(
    string Username,
    string DisplayName,
    string Password,
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
