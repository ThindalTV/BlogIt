namespace BlogIt.Shared;

/// <summary>Well-known keys for the SiteSettings key-value store.</summary>
public static class SettingKeys
{
    public const string SiteName = "SiteName";
    public const string SiteDescription = "SiteDescription";
    public const string SiteUrl = "SiteUrl";
    public const string DefaultOgImage = "DefaultOgImage";

    public const string AzureStorageConnectionString = "AzureStorageConnectionString";
    public const string AzureStorageContainer = "AzureStorageContainer";

    public const string OpenAiApiKey = "OpenAiApiKey";

    // AI provider configuration
    // AiProvider: "openai-compatible" | "github-copilot"
    // For openai-compatible: set AiBaseUrl and AiApiKey and AiModel
    // For github-copilot: set AiApiKey to your GitHub PAT (models are fixed)
    public const string AiProvider = "AiProvider";
    public const string AiBaseUrl = "AiBaseUrl";
    public const string AiApiKey = "AiApiKey";
    public const string AiModel = "AiModel";
    public const string AiExportModel = "AiExportModel";

    public const string JwtSecret = "JwtSecret";
    public const string JwtExpiryMinutes = "JwtExpiryMinutes";

    public const string GoogleAnalyticsMeasurementId = "GoogleAnalyticsMeasurementId";
    public const string GoogleAnalyticsCredentialsJson = "GoogleAnalyticsCredentialsJson";
    public const string GoogleAnalyticsPropertyId = "GoogleAnalyticsPropertyId";

    public const string SetupComplete = "SetupComplete";
}
