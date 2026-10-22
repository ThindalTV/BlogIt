using BlogIt.Contracts.DTOs;

namespace BlogIt.Helpers;

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

        // Blank clears it. Otherwise absolute, because it is written verbatim into og:image and
        // twitter:image, and share-card crawlers ignore a relative URL there without saying so.
        if (!string.IsNullOrWhiteSpace(request.DefaultOgImage)
            && !UrlValidator.IsValidAbsoluteHttpUrl(request.DefaultOgImage.Trim()))
        {
            errors["defaultOgImage"] = ["Default OG image must be an absolute http:// or https:// URL."];
        }

        if (GoogleAnalyticsPropertyIdError(request.GoogleAnalyticsPropertyId) is string propertyError)
            errors["googleAnalyticsPropertyId"] = [propertyError];

        if (GoogleAnalyticsCredentialsError(request.GoogleAnalyticsCredentialsJson) is string credentialsError)
            errors["googleAnalyticsCredentialsJson"] = [credentialsError];

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

    /// <summary>
    /// A GA4 property ID is the bare number from Admin → Property details. The two IDs people paste
    /// instead are both named, because either one is accepted by nothing until the dashboard's first
    /// report call fails with an error that does not mention which field was wrong.
    /// </summary>
    internal static string? GoogleAnalyticsPropertyIdError(string? propertyId)
    {
        if (string.IsNullOrWhiteSpace(propertyId))
            return null;

        var trimmed = propertyId.Trim();
        if (trimmed.StartsWith("properties/", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["properties/".Length..];

        if (trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit))
            return null;

        if (trimmed.StartsWith("G-", StringComparison.OrdinalIgnoreCase))
            return "Property ID must be the numeric GA4 property ID (Admin → Property details), not a "
                + "measurement ID (G-…).";
        if (trimmed.StartsWith("GTM-", StringComparison.OrdinalIgnoreCase))
            return "Property ID must be the numeric GA4 property ID (Admin → Property details), not the "
                + "Tag Manager container ID (GTM-…).";

        return "Property ID must be the numeric GA4 property ID, for example 123456789.";
    }

    /// <summary>
    /// Checks the service-account key is the JSON file Google issues, not just any text. Null,
    /// blank and the redaction placeholder all mean "unchanged" or "clear" and pass.
    /// </summary>
    internal static string? GoogleAnalyticsCredentialsError(string? credentialsJson)
    {
        if (string.IsNullOrWhiteSpace(credentialsJson) || credentialsJson == SettingsRedaction.Placeholder)
            return null;

        const string expected = "Service account credentials must be the JSON key file downloaded from "
            + "Google Cloud (IAM → Service accounts → Keys)";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(credentialsJson);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return expected + ".";

            if (!root.TryGetProperty("type", out var type) || type.GetString() != "service_account")
                return expected + " — this JSON is not a service-account key (\"type\" is not \"service_account\").";

            foreach (var field in (string[])["client_email", "private_key"])
            {
                if (!root.TryGetProperty(field, out var value)
                    || value.ValueKind != System.Text.Json.JsonValueKind.String
                    || string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return expected + $" — \"{field}\" is missing.";
                }
            }

            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return expected + " — the text is not valid JSON.";
        }
    }
}
