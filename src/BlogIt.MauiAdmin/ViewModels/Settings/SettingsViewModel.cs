using BlogIt.MauiAdmin.Services;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Settings;

public partial class SettingsViewModel(MauiApiClient apiClient) : ObservableObject
{
    private const int MinJwtExpiryMinutes = 5;
    private const int MaxJwtExpiryMinutes = 10080;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? statusMessage;

    // Site
    [ObservableProperty] private string siteName = string.Empty;
    [ObservableProperty] private string siteUrl = string.Empty;
    [ObservableProperty] private string siteDescription = string.Empty;
    [ObservableProperty] private string defaultOgImage = string.Empty;

    // AI — ApiKey starts blank regardless of the server's redacted "***" value;
    // it is only sent back if the user actually retypes something this session.
    [ObservableProperty] private string aiProvider = "openai-compatible";
    [ObservableProperty] private string aiBaseUrl = string.Empty;
    [ObservableProperty] private string aiApiKey = string.Empty;
    [ObservableProperty] private string aiModel = string.Empty;
    [ObservableProperty] private string aiExportModel = string.Empty;

    // Analytics — CredentialsJson has the same blank-unless-retyped rule as AiApiKey.
    [ObservableProperty] private string gaMeasurementId = string.Empty;
    [ObservableProperty] private string gaPropertyId = string.Empty;
    [ObservableProperty] private string gaCredentialsJson = string.Empty;

    // Auth
    [ObservableProperty] private string jwtExpiryMinutesText = "1440";

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await apiClient.GetSettingsAsync();
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            var settings = result.Value!;
            SiteName = Get(settings, SettingKeys.SiteName);
            SiteUrl = Get(settings, SettingKeys.SiteUrl);
            SiteDescription = Get(settings, SettingKeys.SiteDescription);
            DefaultOgImage = Get(settings, SettingKeys.DefaultOgImage);

            AiProvider = Get(settings, SettingKeys.AiProvider, "openai-compatible");
            AiBaseUrl = Get(settings, SettingKeys.AiBaseUrl);
            AiModel = Get(settings, SettingKeys.AiModel);
            AiExportModel = Get(settings, SettingKeys.AiExportModel);
            // AiApiKey deliberately left blank — never populated from the redacted "***".

            GaMeasurementId = Get(settings, SettingKeys.GoogleAnalyticsMeasurementId);
            GaPropertyId = Get(settings, SettingKeys.GoogleAnalyticsPropertyId);
            // GaCredentialsJson deliberately left blank, same reason as AiApiKey.

            JwtExpiryMinutesText = Get(settings, SettingKeys.JwtExpiryMinutes, "1440");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Get(Dictionary<string, string> settings, string key, string fallback = "") =>
        settings.TryGetValue(key, out var value) ? value : fallback;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;

        // The server validates this too now, and rejects out-of-range values rather than
        // storing them; clamping here keeps the local field consistent with what it will accept.
        if (!int.TryParse(JwtExpiryMinutesText, out var jwtMinutes))
        {
            ErrorMessage = "JWT expiry must be a whole number of minutes.";
            return;
        }
        jwtMinutes = Math.Clamp(jwtMinutes, MinJwtExpiryMinutes, MaxJwtExpiryMinutes);
        JwtExpiryMinutesText = jwtMinutes.ToString();

        // Secrets stay null unless retyped this session — null means "leave unchanged", so
        // neither a blank field nor a redacted "***" can overwrite a stored credential.
        var toSave = new SiteSettingsUpdateRequest(
            SiteName: SiteName,
            SiteUrl: SiteUrl,
            SiteDescription: SiteDescription,
            DefaultOgImage: DefaultOgImage,
            AiProvider: AiProvider,
            AiBaseUrl: AiBaseUrl,
            AiModel: AiModel,
            AiExportModel: AiExportModel,
            AiApiKey: string.IsNullOrWhiteSpace(AiApiKey) ? null : AiApiKey,
            GoogleAnalyticsMeasurementId: GaMeasurementId,
            GoogleAnalyticsPropertyId: GaPropertyId,
            GoogleAnalyticsCredentialsJson: string.IsNullOrWhiteSpace(GaCredentialsJson) ? null : GaCredentialsJson,
            JwtExpiryMinutes: jwtMinutes);

        IsBusy = true;
        try
        {
            var result = await apiClient.UpdateSettingsAsync(toSave);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            AiApiKey = string.Empty;
            GaCredentialsJson = string.Empty;
            StatusMessage = "Saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
