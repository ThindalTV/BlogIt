using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Sites;

public partial class SetupRequiredViewModel(
    SiteProfileService profileService,
    SiteProbeService probeService,
    IDialogService dialogService) : ObservableObject, IQueryAttributable
{
    private string _siteId = string.Empty;
    private Uri? _baseUri;
    private string _apiPath = "/api";

    [ObservableProperty]
    private string siteLabel = "this site";

    [ObservableProperty]
    private bool isBusy;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var idObj) && idObj is string id)
            _ = LoadSiteAsync(id);
    }

    private async Task LoadSiteAsync(string id)
    {
        _siteId = id;
        var profiles = await profileService.GetProfilesAsync();
        var profile = profiles.FirstOrDefault(p => p.Id == id);
        if (profile is null) return;

        SiteLabel = profile.DisplayLabel;
        _baseUri = profile.BaseUri;
        _apiPath = profile.ApiPath;
    }

    [RelayCommand]
    private async Task OpenSetupAsync()
    {
        if (_baseUri is null) return;
        await Browser.Default.OpenAsync(new Uri(_baseUri, "blogit/"), BrowserLaunchMode.SystemPreferred);
    }

    [RelayCommand]
    private async Task CheckAgainAsync()
    {
        if (_baseUri is null) return;

        IsBusy = true;
        try
        {
            var result = await probeService.ProbeAsync(_baseUri, _apiPath);
            if (result.Status == SiteProbeStatus.ReachableSetupComplete)
                await Shell.Current.GoToAsync($"sites/login?id={_siteId}");
            else if (result.Status == SiteProbeStatus.ReachableSetupIncomplete)
                await dialogService.AlertAsync("Still not finished", "Setup hasn't been completed on this site yet.");
            else
                await dialogService.AlertAsync("Can't reach this site", "Check your connection and try again.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
