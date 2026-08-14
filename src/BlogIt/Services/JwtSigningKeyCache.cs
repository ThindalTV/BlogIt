using System.Text;
using BlogIt.Shared;
using Microsoft.IdentityModel.Tokens;

namespace BlogIt.Services;

/// <summary>
/// Holds the current JWT signing key, rebuilding it only when the underlying secret actually
/// changes. Used via <see cref="TokenValidationParameters.IssuerSigningKeyResolver"/> instead of
/// mutating the shared, singleton <c>JwtBearerOptions.TokenValidationParameters</c> on every
/// request — that mutation added an avoidable settings round trip per request and raced against
/// in-flight validations if the secret was ever rotated while requests were in flight. Reads and
/// writes go through a single atomic field of an immutable snapshot, so concurrent requests never
/// observe a half-updated key.
/// </summary>
internal sealed class JwtSigningKeyCache(ISettingsService settings)
{
    private volatile CachedKey? cached;

    public async Task RefreshAsync()
    {
        var secret = await settings.GetAsync(SettingKeys.JwtSecret);
        if (string.IsNullOrEmpty(secret))
            return;

        if (cached is { } current && current.Secret == secret)
            return;

        cached = new CachedKey(secret, new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)));
    }

    public IEnumerable<SecurityKey> ResolveKeys() =>
        cached is { } current ? [current.Key] : [];

    private sealed record CachedKey(string Secret, SymmetricSecurityKey Key);
}
