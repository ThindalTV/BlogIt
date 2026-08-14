using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
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

            if (!UrlValidator.IsValidAbsoluteHttpUrl(request.SiteUrl))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["siteUrl"] = ["Site URL must be an absolute http:// or https:// URL."]
                });
            }

            if (PasswordPolicy.Validate(request.Password) is string passwordError)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["password"] = [passwordError]
                });
            }

            // Guards against two concurrent /setup/initialize requests both passing the
            // AnyAsync() check above before either commits: SetupLock.Id is a fixed value (1),
            // so at most one of two racing inserts can win the SaveChangesAsync call below — the
            // loser hits a primary key violation there, and (on a real relational provider —
            // SQL Server, Azure SQL) its AppUser insert rolls back with it as part of the same
            // implicit transaction. Added before the AppUser below so the lock claim is settled
            // first if the two ever need to be split into separate calls later.
            db.SetupLocks.Add(new SetupLock());

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

            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
            {
                // A concurrent request already won the SetupLock insert. Real relational
                // providers (SQL Server, Azure SQL) throw DbUpdateException for the resulting
                // PK violation; EF Core's InMemory provider (used in tests) throws a bare
                // ArgumentException for the same duplicate-key case instead of wrapping it.
                return Results.Conflict("Setup has already been completed.");
            }

            await settings.SetManyAsync(settingsToSave);

            return Results.Ok(new { message = "Setup complete." });
        });

        return app;
    }
}
