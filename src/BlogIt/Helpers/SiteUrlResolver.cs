using Microsoft.AspNetCore.Http;

namespace BlogIt.Shared.Helpers;

public static class SiteUrlResolver
{
    /// <summary>
    /// Resolves the site's public base URL: the operator-configured value from
    /// <c>ISettingsService</c> (set via Setup/Settings) first, falling back to
    /// <c>IConfiguration</c>, and only falling back to the incoming request's <c>Host</c> header
    /// — which is attacker-controllable and must not be trusted for anything cached or
    /// crawler-facing — when neither is configured. Returns an absolute URL with a trailing
    /// slash.
    /// </summary>
    public static string Resolve(string? settingsSiteUrl, string? configurationSiteUrl, HttpRequest request)
    {
        foreach (var candidate in new[] { settingsSiteUrl, configurationSiteUrl })
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var configuredUri) &&
                (configuredUri.Scheme == Uri.UriSchemeHttp || configuredUri.Scheme == Uri.UriSchemeHttps))
            {
                return configuredUri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
            }
        }

        var origin = $"{request.Scheme}://{request.Host}{request.PathBase}/";
        if (Uri.TryCreate(origin, UriKind.Absolute, out var requestUri) &&
            (requestUri.Scheme == Uri.UriSchemeHttp || requestUri.Scheme == Uri.UriSchemeHttps))
        {
            return requestUri.AbsoluteUri;
        }

        throw new InvalidOperationException("A valid HTTP(S) site URL or request origin is required.");
    }
}
