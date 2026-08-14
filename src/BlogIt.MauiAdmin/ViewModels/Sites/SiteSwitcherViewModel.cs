using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Models;
using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Sites;

/// <summary>Backs the pinned header above the desktop nav rail's item list, so
/// switching sites never requires leaving it.</summary>
public partial class SiteSwitcherViewModel : ObservableObject
{
    private readonly SiteProfileService _profileService;

    [ObservableProperty]
    private string activeSiteLabel = "No site added";

    public ObservableCollection<SiteProfile> Sites { get; } = [];

    public SiteSwitcherViewModel(SiteProfileService profileService)
    {
        _profileService = profileService;
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
        await Shell.Current.GoToAsync("//dashboard");
    }

    [RelayCommand]
    private async Task ManageAsync() => await Shell.Current.GoToAsync("//sites");
}
