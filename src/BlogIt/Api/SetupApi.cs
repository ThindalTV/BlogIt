using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Services;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Api;

public static class SetupApi
{
    public static IEndpointRouteBuilder MapSetupApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/setup")
            .WithTags("Setup")
            .AllowAnonymous();

        group.MapGet("/status", async (BlogItDbContext db) =>
        {
            var hasUser = await db.Users.AnyAsync();
            var isComplete = hasUser;
            return Results.Ok(new SetupStatusResponse(isComplete));
        });

        group.MapPost("/initialize", async (
            SetupInitializeRequest request,
            BlogItDbContext db,
            ISettingsService settings) =>
        {
            if (await db.Users.AnyAsync())
                return Results.Conflict("Setup has already been completed.");

            var user = new AppUser
            {
                Username = request.Username,
                DisplayName = request.DisplayName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
            };
            db.Users.Add(user);

            var settingsToSave = new Dictionary<string, string>
            {
                [SettingKeys.SiteName] = request.SiteName,
                [SettingKeys.SiteUrl] = request.SiteUrl,
                [SettingKeys.SiteDescription] = request.SiteDescription,
                [SettingKeys.AiProvider] = request.AiProvider,
                [SettingKeys.AiApiKey] = request.AiApiKey,
                [SettingKeys.JwtSecret] = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                [SettingKeys.JwtExpiryMinutes] = "1440",
                [SettingKeys.SetupComplete] = "true"
            };

            if (!string.IsNullOrWhiteSpace(request.AiBaseUrl))
                settingsToSave[SettingKeys.AiBaseUrl] = request.AiBaseUrl;

            if (!string.IsNullOrWhiteSpace(request.AiModel))
                settingsToSave[SettingKeys.AiModel] = request.AiModel;

            if (!string.IsNullOrWhiteSpace(request.AiExportModel))
                settingsToSave[SettingKeys.AiExportModel] = request.AiExportModel;

            if (!string.IsNullOrWhiteSpace(request.DefaultOgImage))
                settingsToSave[SettingKeys.DefaultOgImage] = request.DefaultOgImage;

            if (!string.IsNullOrWhiteSpace(request.GoogleAnalyticsMeasurementId))
                settingsToSave[SettingKeys.GoogleAnalyticsMeasurementId] = request.GoogleAnalyticsMeasurementId;

            if (!string.IsNullOrWhiteSpace(request.GoogleAnalyticsPropertyId))
                settingsToSave[SettingKeys.GoogleAnalyticsPropertyId] = request.GoogleAnalyticsPropertyId;

            if (!string.IsNullOrWhiteSpace(request.GoogleAnalyticsCredentialsJson))
                settingsToSave[SettingKeys.GoogleAnalyticsCredentialsJson] = request.GoogleAnalyticsCredentialsJson;

            await db.SaveChangesAsync();
            await settings.SetManyAsync(settingsToSave);

            return Results.Ok(new { message = "Setup complete." });
        });

        return app;
    }
}
