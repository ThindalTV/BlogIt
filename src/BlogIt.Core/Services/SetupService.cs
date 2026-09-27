using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Services;

/// <summary>What happened to a first-run setup attempt.</summary>
public enum SetupOutcome
{
    /// <summary>The site was claimed: the administrator account and settings now exist.</summary>
    Completed,

    /// <summary>Setup had already been completed, so nothing was changed.</summary>
    AlreadyComplete,

    /// <summary>The request was rejected; see <see cref="SetupResult.Errors"/>.</summary>
    ValidationFailed
}

/// <param name="Errors">
/// Field-keyed validation messages, populated only for <see cref="SetupOutcome.ValidationFailed"/>.
/// </param>
public sealed record SetupResult(
    SetupOutcome Outcome,
    IReadOnlyDictionary<string, string[]>? Errors = null);

/// <summary>
/// First-run setup: claiming an unclaimed site by creating its first administrator and settings.
/// </summary>
/// <remarks>
/// Extracted from the setup endpoint so the HTTP route and the programmatic
/// <c>InitializeBlogItAsync</c> entry point run the same code rather than two copies of it. The
/// endpoint is now only a translation of <see cref="SetupResult"/> into status codes.
/// </remarks>
public interface ISetupService
{
    /// <summary>Whether the site has already been claimed.</summary>
    Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>Claims the site, if it has not been claimed already.</summary>
    Task<SetupResult> InitializeAsync(
        SetupInitializeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SetupService(
    BlogItDbContext db,
    ISettingsService settings,
    BlogItOptions options) : ISetupService
{
    /// <remarks>
    /// The existence of a user is the signal, not the <c>SetupComplete</c> setting: a site with an
    /// administrator is claimed whether or not any flag says so, and there is exactly one way to
    /// create the first one.
    /// </remarks>
    public async Task<bool> IsCompleteAsync(CancellationToken cancellationToken = default) =>
        await db.Users.AnyAsync(cancellationToken);

    public async Task<SetupResult> InitializeAsync(
        SetupInitializeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
            return new SetupResult(SetupOutcome.AlreadyComplete);

        if (Validate(request) is { Count: > 0 } errors)
            return new SetupResult(SetupOutcome.ValidationFailed, errors);

        // Guards against two concurrent initializations both passing the AnyAsync() check above
        // before either commits: SetupLock.Id is a fixed value (1), so at most one of two racing
        // inserts can win the SaveChangesAsync call below — the loser hits a primary key violation
        // there, and (on a real relational provider) its AppUser insert rolls back with it as part
        // of the same implicit transaction. This works identically whether the loser is a second
        // HTTP request or a second container replica.
        db.SetupLocks.Add(new SetupLock());

        db.Users.Add(new AppUser
        {
            Username = request.Username,
            DisplayName = request.DisplayName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
        });

        var settingsToSave = BuildSettings(request);

        // Written as entities in this same SaveChangesAsync rather than through ISettingsService
        // afterwards, so the account and the settings commit together.
        //
        // They used to be two transactions, and the gap between them was unrecoverable: a crash
        // after the user was committed but before JwtSecret was written left a site with an
        // administrator nobody could log in as — no secret to sign a token with — and setup could
        // never be re-run, because a user now existed. That window is small but it is a permanent
        // brick, and running setup unattended from a container entry point is exactly the situation
        // that makes an interrupted process likely rather than theoretical.
        foreach (var (key, value) in settingsToSave)
            db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
        {
            // A concurrent initialization already won the SetupLock insert. Real relational
            // providers throw DbUpdateException for the resulting PK violation; EF Core's InMemory
            // provider (used in tests) throws a bare ArgumentException for the same duplicate-key
            // case instead of wrapping it.
            return new SetupResult(SetupOutcome.AlreadyComplete);
        }

        // The rows are already committed above; this is only to refresh the settings cache held by
        // the singleton, which would otherwise not see its own site's settings until something else
        // wrote one. Idempotent — it overwrites identical values.
        await settings.SetManyAsync(settingsToSave);

        return new SetupResult(SetupOutcome.Completed);
    }

    /// <summary>
    /// Every validation the setup request must pass, in the order their errors are worth reporting.
    /// </summary>
    /// <remarks>
    /// Shared by both entry points on purpose: the anonymous HTTP route is how an unclaimed site
    /// gets claimed, so it must not be a way around a rule the programmatic path enforces, or the
    /// other way round.
    /// </remarks>
    private Dictionary<string, string[]> Validate(SetupInitializeRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (!UrlValidator.IsValidAbsoluteHttpUrl(request.SiteUrl))
            errors["siteUrl"] = ["Site URL must be an absolute http:// or https:// URL."];

        // The same AI endpoint policy the authenticated settings route applies.
        if (SiteSettingsValidator.Validate(
                new SiteSettingsUpdateRequest(AiBaseUrl: request.AiBaseUrl),
                options.AllowPrivateAiEndpoints)
            is { Count: > 0 } aiErrors)
        {
            foreach (var (key, value) in aiErrors)
                errors[key] = value;
        }

        // Analytics reporting may only be set up alongside a tag container — see AnalyticsPolicy
        // for why the two are one decision rather than two.
        if (AnalyticsPolicy.Validate(
                request.GoogleTagManagerContainerId,
                request.GoogleAnalyticsPropertyId,
                request.GoogleAnalyticsCredentialsJson)
            is string analyticsError)
        {
            errors["googleTagManagerContainerId"] = [analyticsError];
        }

        if (PasswordPolicy.Validate(request.Password) is string passwordError)
            errors["password"] = [passwordError];

        // Same two fields UsersApi validates, and it matters more here: this is the account the site
        // owner is locked out of if it is written wrong, and there is no second admin to fix it.
        if (AccountFieldValidator.Validate(request.Username, request.DisplayName)
            is { Count: > 0 } accountErrors)
        {
            foreach (var (key, value) in accountErrors)
                errors[key] = value;
        }

        return errors;
    }

    /// <remarks>
    /// Every optional field is written unconditionally, as null when the caller left it out. Null is
    /// how "not set" is stored, so a site set up without AI or analytics ends up with those keys
    /// present and null rather than absent — the same state clearing them later produces.
    /// </remarks>
    private static Dictionary<string, string?> BuildSettings(SetupInitializeRequest request) => new()
    {
        [SettingKeys.SiteName] = request.SiteName,
        [SettingKeys.SiteUrl] = request.SiteUrl,
        [SettingKeys.SiteDescription] = OptionalText.OrNull(request.SiteDescription),
        [SettingKeys.AiProvider] = OptionalText.OrNull(request.AiProvider),
        [SettingKeys.AiApiKey] = OptionalText.OrNull(request.AiApiKey),
        [SettingKeys.AiBaseUrl] = OptionalText.OrNull(request.AiBaseUrl),
        [SettingKeys.AiModel] = OptionalText.OrNull(request.AiModel),
        [SettingKeys.AiExportModel] = OptionalText.OrNull(request.AiExportModel),
        [SettingKeys.DefaultOgImage] = OptionalText.OrNull(request.DefaultOgImage),
        [SettingKeys.GoogleTagManagerContainerId] =
            OptionalText.OrNull(request.GoogleTagManagerContainerId?.Trim()),
        [SettingKeys.GoogleAnalyticsPropertyId] =
            OptionalText.OrNull(request.GoogleAnalyticsPropertyId),
        [SettingKeys.GoogleAnalyticsCredentialsJson] =
            OptionalText.OrNull(request.GoogleAnalyticsCredentialsJson),
        [SettingKeys.JwtSecret] = JwtSecretGenerator.Generate(),
        [SettingKeys.JwtExpiryMinutes] = "1440"
    };
}
