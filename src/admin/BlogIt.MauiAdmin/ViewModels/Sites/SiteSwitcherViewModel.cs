using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Core.Navigation;
using BlogIt.MauiAdmin.Core.Sites;
using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Sites;

/// <summary>Backs the site switcher pinned above the navigation menu. Because there is one Shell
/// at every window size, this header is present on every platform — it used to be declared only
/// in the desktop Shell, which left tablets and macOS with no way to change blogs short of the
/// Sites screen. Switching sites never requires leaving the screen you are on.</summary>
public partial class SiteSwitcherViewModel : ObservableObject
{
    private readonly SiteProfileService _profileService;
    private readonly SiteActivator _activator;

    [ObservableProperty]
    private string activeSiteLabel = "No site added";

    public ObservableCollection<SiteProfile> Sites { get; } = [];

    public SiteSwitcherViewModel(SiteProfileService profileService, SiteActivator activator)
    {
        _profileService = profileService;
        _activator = activator;
        _profileService.OnChanged += () => _ = RefreshAsync();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var profiles = await _profileService.GetProfilesAsync();
        var active = await _profileService.GetActiveProfileAsync();

        Sites.Clear();
        foreach (var p in profiles)
            Sites.Add(p);

        ActiveSiteLabel = active?.DisplayLabel ?? "No site added";
    }

    /// <summary>Switches to <paramref name="site"/> via the shared activation path, so a site
    /// whose session has expired goes to its login screen rather than to a dashboard that will
    /// only 401.</summary>
    [RelayCommand]
    private async Task SwitchAsync(SiteProfile site) => await _activator.ActivateAsync(site);

    [RelayCommand]
    private async Task ManageAsync() => await Shell.Current.GoToAsync(AppNavigation.RouteTo("sites"));
}
