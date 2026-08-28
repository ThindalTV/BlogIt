using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Core.Navigation;
using BlogIt.MauiAdmin.Core.Sites;
using BlogIt.MauiAdmin.Models;
using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Sites;

/// <summary>Backs the site switcher: the pinned header above the desktop and compact nav
/// rails, and the header of the phone's More tab. Switching sites never requires leaving the
/// screen you are on.</summary>
public partial class SiteSwitcherViewModel : ObservableObject
{
    private readonly SiteProfileService _profileService;
    private readonly DestinationRouter _router;

    [ObservableProperty]
    private string activeSiteLabel = "No site added";

    public ObservableCollection<SiteProfile> Sites { get; } = [];

    public SiteSwitcherViewModel(SiteProfileService profileService, DestinationRouter router)
    {
        _profileService = profileService;
        _router = router;
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

    [RelayCommand]
    private async Task SwitchAsync(SiteProfile site)
    {
        await _profileService.SetActiveAsync(site.Id);
        await Shell.Current.GoToAsync(_router.RouteTo("dashboard"));
    }

    [RelayCommand]
    private async Task ManageAsync() => await Shell.Current.GoToAsync(_router.RouteTo("sites"));
}
