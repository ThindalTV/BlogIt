namespace BlogIt.MauiAdmin.Models;

/// <summary>Represents a saved site connection: a domain (+ optional port), independent
/// of any URL string, since the Add-Blog form only ever collects a domain and port.
/// The JWT itself is never stored on this object — it lives in SecureStorage keyed by
/// <see cref="Id"/> (see <see cref="Services.SiteProfileService"/>) so that a decrypt
/// failure for one site can't take down every site's session.</summary>
public class SiteProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>User-facing label. Defaults to <see cref="Host"/> when left blank.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Bare domain, e.g. "myblog.com" — no scheme, no port, no path.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Null means "use the default port for the scheme" (443 for https, 80 for http).</summary>
    public int? Port { get; set; }

    public bool UseHttps { get; set; } = true;

    /// <summary>Power-user override for a customized server ApiPath; null means the
    /// server default "/api".</summary>
    public string? ApiPathOverride { get; set; }

    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>Metadata flag only — whether a JWT is currently stored in SecureStorage
    /// for this site. The actual token is never serialized onto this object.</summary>
    public bool HasStoredToken { get; set; }

    public bool IsTokenValid =>
        HasStoredToken && TokenExpiresAt is { } exp && exp > DateTime.UtcNow.AddMinutes(1);

    public string ApiPath => string.IsNullOrWhiteSpace(ApiPathOverride) ? "/api" : ApiPathOverride!;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Name) ? Host : Name;

    public Uri BaseUri => new($"{(UseHttps ? "https" : "http")}://{HostAndPort}/");

    private string HostAndPort => Port is { } p ? $"{Host}:{p}" : Host;
}
