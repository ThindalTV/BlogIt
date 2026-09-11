using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Helpers;
using BlogIt.Services;

namespace BlogIt.Api;

public static class SettingsApi
{
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
        group.MapPost("/jwt-secret/rotate", RotateJwtSecret);
        group.MapGet("/ai-provider", GetAiProvider);

        return app;
    }

    private static async Task<IResult> GetSettings(ISettingsService settings)
    {
        var all = await settings.GetAllAsync();
        var redacted = all.ToDictionary(
            kvp => kvp.Key,
            kvp => SensitiveKeys.Contains(kvp.Key) && !string.IsNullOrEmpty(kvp.Value)
                ? SettingsRedaction.Placeholder
                : kvp.Value
        );
        return Results.Ok(redacted);
    }

    /// <summary>
    /// Writes the settings a client is allowed to change. The body is
    /// <see cref="SiteSettingsUpdateRequest"/> rather than a free-form dictionary specifically so
    /// that <c>JwtSecret</c> and the Azure storage keys have no property to arrive through — the
    /// first can lock every user out permanently with no in-app recovery, and the other two are
    /// application configuration. Rotate the signing secret through
    /// <see cref="RotateJwtSecret"/> instead.
    /// </summary>
    private static async Task<IResult> SaveSettings(
        SiteSettingsUpdateRequest body,
        ISettingsService settings,
        BlogItOptions options)
    {
        var errors = SiteSettingsValidator.Validate(body, options.AllowPrivateAiEndpoints);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        // Values here are never null: Set/SetSecret skip a null, which on this path means "the
        // client did not send this field" — leave the stored value alone. That is a different
        // meaning from the null the storage layer uses for "not set", and deliberately so.
        var toSave = new Dictionary<string, string?>();
        Set(toSave, SettingKeys.SiteName, body.SiteName);
        Set(toSave, SettingKeys.SiteUrl, body.SiteUrl);
        Set(toSave, SettingKeys.SiteDescription, body.SiteDescription);
        Set(toSave, SettingKeys.DefaultOgImage, body.DefaultOgImage);
        Set(toSave, SettingKeys.AiProvider, body.AiProvider?.Trim().ToLowerInvariant());
        Set(toSave, SettingKeys.AiBaseUrl, body.AiBaseUrl);
        Set(toSave, SettingKeys.AiModel, body.AiModel);
        Set(toSave, SettingKeys.AiExportModel, body.AiExportModel);
        Set(toSave, SettingKeys.GoogleTagManagerContainerId, body.GoogleTagManagerContainerId?.Trim());
        Set(toSave, SettingKeys.GoogleAnalyticsPropertyId, body.GoogleAnalyticsPropertyId);
        SetSecret(toSave, SettingKeys.AiApiKey, body.AiApiKey);
        SetSecret(toSave, SettingKeys.GoogleAnalyticsCredentialsJson, body.GoogleAnalyticsCredentialsJson);

        if (body.JwtExpiryMinutes is int minutes)
            toSave[SettingKeys.JwtExpiryMinutes] = minutes.ToString();

        // Analytics reporting may only exist alongside a tag container - see AnalyticsPolicy.
        // Checked against the *effective* settings rather than the body, because a partial update
        // carrying only a property ID says nothing about the container ID already stored, and a
        // body-only check would wave it through. Reading the stored values here rather than up with
        // the other validation keeps the merge rules in one place: toSave already encodes
        // Set/SetSecret's "null means unchanged" and "the placeholder means unchanged".
        var stored = await settings.GetAllAsync();
        if (AnalyticsPolicy.Validate(
                Effective(SettingKeys.GoogleTagManagerContainerId),
                Effective(SettingKeys.GoogleAnalyticsPropertyId),
                Effective(SettingKeys.GoogleAnalyticsCredentialsJson))
            is string analyticsError)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["googleTagManagerContainerId"] = [analyticsError]
            });
        }

        if (toSave.Count > 0)
            await settings.SetManyAsync(toSave);

        return Results.NoContent();

        // The value a key will hold once this write lands: what the request is changing it to, or
        // what is already stored when the request leaves it alone.
        string? Effective(string key) =>
            toSave.TryGetValue(key, out var pending) ? pending : stored.GetValueOrDefault(key);
    }

    /// <summary>
    /// Replaces the JWT signing secret with a freshly generated one. The new value is never
    /// returned — the only thing a caller learns is that rotation succeeded.
    /// </summary>
    /// <remarks>
    /// Every token signed with the previous secret stops validating immediately, including the
    /// caller's own, so the client that invokes this has to send its user back to the login
    /// screen. That is the point of the endpoint: it is the recovery path for a leaked secret.
    /// </remarks>
    private static async Task<IResult> RotateJwtSecret(ISettingsService settings)
    {
        await settings.SetAsync(SettingKeys.JwtSecret, JwtSecretGenerator.Generate());
        return Results.NoContent();
    }

    /// <summary>Null means "leave this setting alone"; an empty string clears it.</summary>
    private static void Set(Dictionary<string, string?> target, string key, string? value)
    {
        if (value is not null)
            target[key] = value;
    }

    /// <summary>
    /// As <see cref="Set"/>, but also treats the redaction placeholder as "leave alone". GET
    /// masks secrets to that literal, and a client that echoes a fetched settings object back
    /// would otherwise overwrite a real credential with three asterisks. Both shipped clients
    /// strip it themselves; this makes a third client that forgets harmless.
    /// </summary>
    private static void SetSecret(Dictionary<string, string?> target, string key, string? value)
    {
        if (value is null || value == SettingsRedaction.Placeholder)
            return;
        target[key] = value;
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
