using BlogIt.MauiAdmin.Models;
using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Sites;

public partial class AddSiteViewModel(SiteProfileService profileService, SiteProbeService probeService)
    : ObservableObject, IQueryAttributable
{
    private SiteProfile? _editingProfile;

    [ObservableProperty]
    private string pageTitle = "Add Blog";

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string domain = string.Empty;

    [ObservableProperty]
    private string portText = string.Empty;

    /// <summary>Default is HTTPS; this only flips to plain HTTP when the user
    /// explicitly opts in under Advanced (e.g. testing against a local dev server).</summary>
    [ObservableProperty]
    private bool useHttp;

    [ObservableProperty]
    private string customApiPath = string.Empty;

    [ObservableProperty]
    private bool isAdvancedExpanded;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private bool isBusy;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var idObj) && idObj is string id)
            _ = LoadForEditAsync(id);
    }

    private async Task LoadForEditAsync(string id)
    {
        var profiles = await profileService.GetProfilesAsync();
        _editingProfile = profiles.FirstOrDefault(p => p.Id == id);
        if (_editingProfile is null) return;

        PageTitle = "Edit Blog";
        Name = _editingProfile.Name;
        Domain = _editingProfile.Host;
        PortText = _editingProfile.Port?.ToString() ?? string.Empty;
        UseHttp = !_editingProfile.UseHttps;
        CustomApiPath = _editingProfile.ApiPathOverride ?? string.Empty;
        IsAdvancedExpanded = !string.IsNullOrEmpty(CustomApiPath);
    }

    [RelayCommand]
    private void ToggleAdvanced() => IsAdvancedExpanded = !IsAdvancedExpanded;

    [RelayCommand]
    private async Task ContinueAsync()
    {
        ErrorMessage = null;

        var (host, port) = NormalizeDomain(Domain, PortText);
        if (string.IsNullOrWhiteSpace(host))
        {
            ErrorMessage = "Enter a domain, e.g. myblog.com.";
            return;
        }

        var useHttps = !UseHttp;
        var apiPathOverride = string.IsNullOrWhiteSpace(CustomApiPath) ? null : CustomApiPath.Trim();

        var profile = _editingProfile ?? new SiteProfile();
        var connectionChanged = _editingProfile is not null &&
            (_editingProfile.Host != host || _editingProfile.Port != port || _editingProfile.UseHttps != useHttps);

        profile.Name = Name.Trim();
        profile.Host = host;
        profile.Port = port;
        profile.UseHttps = useHttps;
        profile.ApiPathOverride = apiPathOverride;

        IsBusy = true;
        try
        {
            var result = await probeService.ProbeAsync(profile.BaseUri, profile.ApiPath);

            if (result.Status == SiteProbeStatus.Unreachable)
            {
                ErrorMessage = "Couldn't reach that address. Check the domain, port, and your connection.";
                return;
            }
            if (result.Status == SiteProbeStatus.NotFound)
            {
                ErrorMessage = "That doesn't look like a BlogIt site. If it uses a customized API path, set it under Advanced.";
                IsAdvancedExpanded = true;
                return;
            }

            if (result.ResolvedApiPath is not null && result.ResolvedApiPath != "/api")
                profile.ApiPathOverride = result.ResolvedApiPath;

            // Changing what a saved site actually points at invalidates any token
            // issued by whatever it used to point at.
            if (connectionChanged)
                await profileService.ClearTokenAsync(profile.Id);

            await profileService.AddOrUpdateProfileAsync(profile);

            await Shell.Current.GoToAsync(result.Status == SiteProbeStatus.ReachableSetupIncomplete
                ? $"sites/setup-required?id={profile.Id}"
                : $"sites/login?id={profile.Id}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static (string Host, int? Port) NormalizeDomain(string domain, string portText)
    {
        var value = domain.Trim();

        // Defensively strip a pasted scheme and trailing path, since users will paste
        // a full URL here even though the form only asks for a domain.
        value = value.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                     .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
        var slashIndex = value.IndexOf('/');
        if (slashIndex >= 0) value = value[..slashIndex];

        int? port = int.TryParse(portText.Trim(), out var explicitPort) ? explicitPort : null;

        // Allow "host:port" shorthand typed directly into the domain field when the
        // separate Port field is left empty.
        var colonIndex = value.LastIndexOf(':');
        if (port is null && colonIndex > 0 && int.TryParse(value[(colonIndex + 1)..], out var shorthandPort))
        {
            port = shorthandPort;
            value = value[..colonIndex];
        }

        return (value, port);
    }
}
