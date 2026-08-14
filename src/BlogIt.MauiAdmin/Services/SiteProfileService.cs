using System.Text.Json;
using BlogIt.MauiAdmin.Models;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// Manages the list of site profiles and which one is currently active. Profile
/// metadata (host/port/username/etc., never a secret) is persisted as one JSON blob in
/// SecureStorage. The JWT itself lives in a separate per-site SecureStorage key
/// ("blogit_jwt_{id}") so that a corrupted/invalidated secret for one site can't wipe
/// every other site's session the way a single shared blob would.
/// </summary>
public class SiteProfileService
{
    private const string ProfilesKey = "blogit_site_profiles";
    private const string ActiveIdKey = "blogit_active_site_id";
    private static string TokenKey(string profileId) => $"blogit_jwt_{profileId}";

    private List<SiteProfile> _profiles = [];
    private string? _activeSiteId;
    private bool _loaded;

    public event Action? OnChanged;

    public async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            var json = await SecureStorage.GetAsync(ProfilesKey);
            if (!string.IsNullOrEmpty(json))
                _profiles = JsonSerializer.Deserialize<List<SiteProfile>>(json) ?? [];

            _activeSiteId = await SecureStorage.GetAsync(ActiveIdKey);
        }
        catch
        {
            _profiles = [];
        }

        _loaded = true;
    }

    public async Task<List<SiteProfile>> GetProfilesAsync()
    {
        await LoadAsync();
        return _profiles;
    }

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

        // Auto-activate if it's the first profile
        if (_profiles.Count == 1)
            await SetActiveAsync(profile.Id);
    }

    public async Task SetActiveAsync(string profileId)
    {
        _activeSiteId = profileId;
        await SecureStorage.SetAsync(ActiveIdKey, profileId);
        OnChanged?.Invoke();
    }

    public async Task DeleteProfileAsync(string profileId)
    {
        await LoadAsync();
        _profiles.RemoveAll(p => p.Id == profileId);
        SecureStorage.Remove(TokenKey(profileId));

        if (_activeSiteId == profileId)
        {
            _activeSiteId = _profiles.FirstOrDefault()?.Id;
            if (_activeSiteId is not null)
                await SecureStorage.SetAsync(ActiveIdKey, _activeSiteId);
            else
                SecureStorage.Remove(ActiveIdKey);
        }
        await PersistAsync();
        OnChanged?.Invoke();
    }

    public async Task SaveTokenAsync(string profileId, string token, DateTime expiresAt,
        string username, string displayName)
    {
        await LoadAsync();
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;

        await SecureStorage.SetAsync(TokenKey(profileId), token);

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
        return await SecureStorage.GetAsync(TokenKey(profileId));
    }

    public async Task ClearTokenAsync(string profileId)
    {
        await LoadAsync();
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;

        SecureStorage.Remove(TokenKey(profileId));
        profile.HasStoredToken = false;
        profile.TokenExpiresAt = null;
        await PersistAsync();
        OnChanged?.Invoke();
    }

    private async Task PersistAsync()
    {
        var json = JsonSerializer.Serialize(_profiles);
        await SecureStorage.SetAsync(ProfilesKey, json);
    }
}
