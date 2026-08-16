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
    /// <param name="settingsSiteUrl">The operator-configured value, if any.</param>
    /// <param name="configurationSiteUrl">The <c>IConfiguration</c> value, if any.</param>
    /// <param name="request">
    /// The current request, or <see langword="null"/> when there is none — a background job or a
    /// cache warmup calling into the public services has no request to fall back to, and gets the
    /// same "configure a site URL" error as a request with an unusable origin.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Nothing is configured and no usable request origin is available.
    /// </exception>
    public static string Resolve(string? settingsSiteUrl, string? configurationSiteUrl, HttpRequest? request)
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

        if (request is not null)
        {
            var origin = $"{request.Scheme}://{request.Host}{request.PathBase}/";
            if (Uri.TryCreate(origin, UriKind.Absolute, out var requestUri) &&
                (requestUri.Scheme == Uri.UriSchemeHttp || requestUri.Scheme == Uri.UriSchemeHttps))
            {
                return requestUri.AbsoluteUri;
            }
        }

        throw new InvalidOperationException("A valid HTTP(S) site URL or request origin is required.");
    }
}
