using System.Text.Json;
using BlogIt.MauiAdmin.Models;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// Manages the list of site profiles and which one is currently active.
/// Profiles are persisted as JSON in SecureStorage.
/// </summary>
public class SiteProfileService
{
    private const string ProfilesKey = "blogit_site_profiles";
    private const string ActiveIdKey = "blogit_active_site_id";

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

        profile.JwtToken = token;
        profile.TokenExpiresAt = expiresAt;
        profile.Username = username;
        profile.DisplayName = displayName;
        await PersistAsync();
    }

    public async Task ClearTokenAsync(string profileId)
    {
        await LoadAsync();
        var profile = _profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null) return;
        profile.JwtToken = null;
        profile.TokenExpiresAt = null;
        await PersistAsync();
    }

    private async Task PersistAsync()
    {
        var json = JsonSerializer.Serialize(_profiles);
        await SecureStorage.SetAsync(ProfilesKey, json);
    }
}
