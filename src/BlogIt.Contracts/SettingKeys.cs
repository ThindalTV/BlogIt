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

    // The tag and the reporting credentials are separate concerns and separate products. The
    // container ID loads Google Tag Manager, which is where tags, triggers and cookie consent are
    // configured; the property ID and service-account JSON are GA4 Data API credentials the admin
    // dashboard reads reports through. See AnalyticsPolicy for why the second requires the first.
    public const string GoogleTagManagerContainerId = "GoogleTagManagerContainerId";
    public const string GoogleAnalyticsCredentialsJson = "GoogleAnalyticsCredentialsJson";
    public const string GoogleAnalyticsPropertyId = "GoogleAnalyticsPropertyId";

    /// <summary>
    /// Written by older versions of setup; never read by anything.
    /// </summary>
    /// <remarks>
    /// Whether setup is complete is decided by whether a user exists (and, for the race, by the
    /// setup lock row). This key was only ever a second, weaker copy of that answer, and the danger
    /// was that it could become the trusted one: a new code path that forgets to write it plus a
    /// reader that believes it is how a claimed site starts reporting itself as unclaimed. Setup no
    /// longer writes it. Existing rows are inert and can be left alone.
    /// </remarks>
    [Obsolete(
        "Setup completion is determined by whether a user exists, not by this key. It is no "
        + "longer written and must not be read.")]
    public const string SetupComplete = "SetupComplete";
}
