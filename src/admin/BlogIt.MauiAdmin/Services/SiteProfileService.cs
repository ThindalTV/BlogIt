using BlogIt.MauiAdmin.Core.Sites;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// The app's site-profile store, bound to MAUI's SecureStorage. All of the actual rules live in
/// <see cref="SiteProfileStore"/> so they can be tested without a device; this type exists to
/// supply the platform storage and to keep the name the rest of the app already injects.
/// </summary>
public class SiteProfileService(SiteProfileStore store)
{
    public event Action? OnChanged
    {
        add => store.OnChanged += value;
        remove => store.OnChanged -= value;
    }

    public Task LoadAsync() => store.LoadAsync();

    public Task<List<SiteProfile>> GetProfilesAsync() => store.GetProfilesAsync();

    public Task<SiteProfile?> GetActiveProfileAsync() => store.GetActiveProfileAsync();

    public Task AddOrUpdateProfileAsync(SiteProfile profile) => store.AddOrUpdateProfileAsync(profile);

    public Task SetActiveAsync(string profileId) => store.SetActiveAsync(profileId);

    public Task DeleteProfileAsync(string profileId) => store.DeleteProfileAsync(profileId);

    public Task SaveTokenAsync(string profileId, string token, DateTimeOffset expiresAt,
        string username, string displayName) =>
        store.SaveTokenAsync(profileId, token, expiresAt, username, displayName);

    public Task<string?> GetTokenAsync(string profileId) => store.GetTokenAsync(profileId);

    public Task ClearTokenAsync(string profileId) => store.ClearTokenAsync(profileId);
}

/// <summary>MAUI SecureStorage behind the store's seam — keychain on iOS/macOS, KeyStore on
/// Android, DPAPI-backed storage on Windows.</summary>
public sealed class MauiSecureStore : ISecureStore
{
    public Task<string?> GetAsync(string key) => SecureStorage.GetAsync(key);

    public Task SetAsync(string key, string value) => SecureStorage.SetAsync(key, value);

    public void Remove(string key) => SecureStorage.Remove(key);
}
