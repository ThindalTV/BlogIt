namespace BlogIt.MauiAdmin.Core.Sites;

/// <summary>
/// The encrypted key/value store site profiles and tokens are kept in — MAUI's SecureStorage in
/// the app, something in-memory in tests.
/// </summary>
/// <remarks>
/// This seam exists so the multi-site rules (which profile is active, which token belongs to
/// which site, what happens to the active site when it is deleted) can be tested at all.
/// SecureStorage is a static platform API backed by the keychain, so code calling it directly is
/// only exercisable on a device.
/// </remarks>
public interface ISecureStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}
