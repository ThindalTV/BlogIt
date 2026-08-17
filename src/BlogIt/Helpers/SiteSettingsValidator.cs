using BlogIt.Shared.DTOs;

namespace BlogIt.Shared.Helpers;

/// <summary>
/// Per-field validation for <see cref="SiteSettingsUpdateRequest"/>, run before anything is
/// written. Only fields the caller actually supplied are checked — null means "unchanged" and is
/// always valid.
/// </summary>
public static class SiteSettingsValidator
{
    /// <summary>Floor for <c>JwtExpiryMinutes</c>; below this, sessions expire faster than a
    /// person can use the admin.</summary>
    public const int MinJwtExpiryMinutes = 5;

    /// <summary>Ceiling for <c>JwtExpiryMinutes</c> (7 days). Nothing revokes a token before its
    /// expiry except a security-stamp bump, so an unbounded value mints a near-immortal
    /// credential — and a large enough one overflows <c>DateTime.AddMinutes</c> outright.</summary>
    public const int MaxJwtExpiryMinutes = 10080;

    /// <summary>The providers <c>AiService.BuildClientsAsync</c> knows how to build a client
    /// for.</summary>
    public static readonly IReadOnlyList<string> KnownAiProviders =
        ["openai-compatible", "github-copilot"];

    /// <summary>
    /// Returns one entry per invalid field, keyed by camelCase field name so the response reads
    /// as a standard validation problem. An empty result means the request is safe to persist.
    /// </summary>
    /// <param name="request">The settings the caller wants written.</param>
    /// <param name="allowPrivateAiEndpoints">
    /// <c>BlogItOptions.AllowPrivateAiEndpoints</c> — whether this deployment permits an AI base URL
    /// on a loopback or private address. Passed as a bool rather than taking the options object so
    /// this stays a pure function over the request.
    /// </param>
    public static Dictionary<string, string[]> Validate(
        SiteSettingsUpdateRequest request,
        bool allowPrivateAiEndpoints = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, string[]>();

        if (request.SiteUrl is not null && !UrlValidator.IsValidAbsoluteHttpUrl(request.SiteUrl))
            errors["siteUrl"] = ["Site URL must be an absolute http:// or https:// URL."];

        // Unlike SiteUrl, blank is meaningful: it clears the override and falls back to the
        // provider's own default endpoint. Anything else has to be a real http(s) URL, because
        // the configured API key is sent to whatever this resolves to.
        if (!string.IsNullOrWhiteSpace(request.AiBaseUrl))
        {
            if (!UrlValidator.IsValidAbsoluteHttpUrl(request.AiBaseUrl))
            {
                errors["aiBaseUrl"] = ["AI base URL must be an absolute http:// or https:// URL."];
            }
            else if (!allowPrivateAiEndpoints
                && UrlValidator.IsPrivateOrLocalHttpUrl(request.AiBaseUrl))
            {
                // The scheme check alone left the SSRF half of the finding open: an absolute http URL
                // pointing at 169.254.169.254 or an internal host still passed, and the stored API key
                // is sent there as a bearer credential. Refused by default; a deployment that really
                // does run its own model nearby opts in once, in host startup, out of reach of anyone
                // who only has admin credentials for the blog.
                errors["aiBaseUrl"] =
                [
                    "AI base URL must not point at a loopback, link-local, or private address. "
                    + "Set BlogItOptions.AllowPrivateAiEndpoints = true to allow a self-hosted model "
                    + "on this machine or private network."
                ];
            }
        }

        if (request.AiProvider is not null
            && !KnownAiProviders.Contains(request.AiProvider.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            errors["aiProvider"] =
                [$"AI provider must be one of: {string.Join(", ", KnownAiProviders)}."];
        }

        if (request.JwtExpiryMinutes is int minutes
            && (minutes < MinJwtExpiryMinutes || minutes > MaxJwtExpiryMinutes))
        {
            errors["jwtExpiryMinutes"] =
                [$"JWT expiry must be between {MinJwtExpiryMinutes} and {MaxJwtExpiryMinutes} minutes."];
        }

        return errors;
    }
}
