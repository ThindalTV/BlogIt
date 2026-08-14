using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Helpers;
using BlogIt.Services;

namespace BlogIt.Api;

public static class SettingsApi
{
    private static readonly HashSet<string> ConfigurationOnlyKeys =
    [
        SettingKeys.AzureStorageConnectionString,
        SettingKeys.AzureStorageContainer,
    ];

    private static readonly HashSet<string> SensitiveKeys =
    [
        SettingKeys.AiApiKey,
        SettingKeys.JwtSecret,
        SettingKeys.GoogleAnalyticsCredentialsJson,
    ];

    public static IEndpointRouteBuilder MapSettingsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/settings")
            .WithTags("Settings")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

        group.MapGet("/", GetSettings);
        group.MapPut("/", SaveSettings);
        group.MapGet("/ai-provider", GetAiProvider);

        return app;
    }

    private static async Task<IResult> GetSettings(ISettingsService settings)
    {
        var all = await settings.GetAllAsync();
        var redacted = all.ToDictionary(
            kvp => kvp.Key,
            kvp => SensitiveKeys.Contains(kvp.Key) && !string.IsNullOrEmpty(kvp.Value) ? "***" : kvp.Value
        );
        return Results.Ok(redacted);
    }

    private static async Task<IResult> SaveSettings(
        Dictionary<string, string> body,
        ISettingsService settings)
    {
        if (body.Keys.Any(ConfigurationOnlyKeys.Contains))
            return Results.BadRequest("Blog storage is configured through application configuration, not site settings.");

        if (body.TryGetValue(SettingKeys.SiteUrl, out var siteUrl) && !UrlValidator.IsValidAbsoluteHttpUrl(siteUrl))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["siteUrl"] = ["Site URL must be an absolute http:// or https:// URL."]
            });
        }

        await settings.SetManyAsync(body);
        return Results.NoContent();
    }

    private static async Task<IResult> GetAiProvider(ISettingsService settings)
    {
        var provider = await settings.GetAsync(SettingKeys.AiProvider);
        var baseUrl = await settings.GetAsync(SettingKeys.AiBaseUrl);
        var model = await settings.GetAsync(SettingKeys.AiModel);
        var exportModel = await settings.GetAsync(SettingKeys.AiExportModel);

        return Results.Ok(new AiProviderInfoDto(
            Provider: provider ?? string.Empty,
            BaseUrl: baseUrl,
            Model: model,
            ExportModel: exportModel
        ));
    }
}
