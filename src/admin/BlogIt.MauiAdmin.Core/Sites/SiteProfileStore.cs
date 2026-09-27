using System.Text.Json;

namespace BlogIt.MauiAdmin.Core.Sites;

/// <summary>
/// Manages the set of blogs the app is connected to and which one is currently active.
/// </summary>
/// <remarks>
/// Profile metadata (host, port, username — never a secret) is persisted as one JSON blob. Each
/// site's JWT is stored under its own key instead, so a corrupted or invalidated secret for one
/// site cannot take down every other site's session the way a single shared blob would. Running
/// several blogs at once is the point of the app, so one bad site must never cost the others.
/// </remarks>
public class SiteProfileStore(ISecureStore store)
{
    private const string ProfilesKey = "blogit_site_profiles";
    private const string ActiveIdKey = "blogit_active_site_id";

    internal static string TokenKey(string profileId) => $"blogit_jwt_{profileId}";

    private List<SiteProfile> _profiles = [];
    private string? _activeSiteId;
    private bool _loaded;

    public event Action? OnChanged;

    public async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            var json = await store.GetAsync(ProfilesKey);
            if (!string.IsNullOrEmpty(json))
                _profiles = JsonSerializer.Deserialize<List<SiteProfile>>(json) ?? [];

            _activeSiteId = await store.GetAsync(ActiveIdKey);
        }
        catch
        {
            // Unreadable profile blob: start from empty rather than failing to launch. The
            // per-site tokens are keyed separately and are unaffected.
            _profiles = [];
        }

        _loaded = true;
    }

    public async Task<List<SiteProfile>> GetProfilesAsync()
    {
        await LoadAsync();
        return _profiles;
    }

    /// <summary>
    /// The active profile, falling back to the first one so the app is never left pointing at no
    /// site while profiles exist.
    /// </summary>
    public async Task<SiteProfile?> GetActiveProfileAsync()
    {
        await LoadAsync();
        return _profiles.FirstOrDefault(p => p.Id == _activeSiteId)
            ?? _profiles.FirstOrDefault();
    }

    public async Task AddOrUpdateProfileAsync(SiteProfile profile)
    {
        await LoadAsync();
        var existing = _profiles.FirstOrDefault(p => p.Id == profile.Id);
        if (existing is not null)
            _profiles.Remove(existing);
        _profiles.Add(profile);
        await PersistAsync();

        // The first site added becomes active; there is nothing else it could be.
        if (_profiles.Count == 1)
            await SetActiveAsync(profile.Id);
    }

    public async Task SetActiveAsync(string profileId)
    {
        _activeSiteId = profileId;
        await store.SetAsync(ActiveIdKey, profileId);
        OnChanged?.Invoke();
    }

    public async Task DeleteProfileAsync(string profileId)
    {
        await LoadAsync();
        _profiles.RemoveAll(p => p.Id == profileId);
        store.Remove(TokenKey(profileId));

        if (_activeSiteId == profileId)
        {
            _activeSiteId = _profiles.FirstOrDefault()?.Id;
            if (_activeSiteId is not null)
                await store.SetAsync(ActiveIdKey, _activeSiteId);
            else
                store.Remove(ActiveIdKey);
        }
        await PersistAsync();
        OnChanged?.Invoke();
    }

    public async Task SaveTokenAsync(string profileId, string token, DateTimeOffset expiresAt,
        string username, string displayName)
    {
        await LoadAsync();
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;

        await store.SetAsync(TokenKey(profileId), token);

        profile.HasStoredToken = true;
        profile.TokenExpiresAt = expiresAt;
        profile.Username = username;
        profile.DisplayName = displayName;
        await PersistAsync();
        OnChanged?.Invoke();
    }

    public async Task<string?> GetTokenAsync(string profileId)
    {
        await LoadAsync();
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null || !profile.HasStoredToken) return null;
        return await store.GetAsync(TokenKey(profileId));
    }

    public async Task ClearTokenAsync(string profileId)
    {
        await LoadAsync();
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;

        store.Remove(TokenKey(profileId));
        profile.HasStoredToken = false;
        profile.TokenExpiresAt = null;
        await PersistAsync();
        OnChanged?.Invoke();
    }

    private async Task PersistAsync() =>
        await store.SetAsync(ProfilesKey, JsonSerializer.Serialize(_profiles));
}
