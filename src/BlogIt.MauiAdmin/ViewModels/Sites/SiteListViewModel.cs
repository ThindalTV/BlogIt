using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Models;
using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Sites;

public partial class SiteListViewModel(
    SiteProfileService profileService,
    SiteProbeService probeService,
    IDialogService dialogService) : ObservableObject
{
    public ObservableCollection<SiteProfile> Sites { get; } = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var profiles = await profileService.GetProfilesAsync();
        Sites.Clear();
        foreach (var p in profiles)
            Sites.Add(p);
    }

    [RelayCommand]
    private async Task AddAsync() => await Shell.Current.GoToAsync("sites/add");

    [RelayCommand]
    private async Task EditAsync(SiteProfile site) => await Shell.Current.GoToAsync($"sites/add?id={site.Id}");

    [RelayCommand]
    private async Task ActivateAsync(SiteProfile site)
    {
        if (site.IsTokenValid)
        {
            await profileService.SetActiveAsync(site.Id);
            await Shell.Current.GoToAsync("//dashboard");
            return;
        }

        // Setup-complete/incomplete is never trusted from a cached flag — re-probe
        // live every time a not-signed-in site is tapped, since setup could have
        // been finished out-of-band since the site was added.
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await probeService.ProbeAsync(site.BaseUri, site.ApiPath);
            switch (result.Status)
            {
                case SiteProbeStatus.ReachableSetupComplete:
                    await Shell.Current.GoToAsync($"sites/login?id={site.Id}");
                    break;
                case SiteProbeStatus.ReachableSetupIncomplete:
                    await Shell.Current.GoToAsync($"sites/setup-required?id={site.Id}");
                    break;
                default:
                    await dialogService.AlertAsync("Can't reach this site", "Check the domain, port, and your connection, then try again.");
                    break;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(SiteProfile site)
    {
        var confirmed = await dialogService.ConfirmAsync(
            "Remove site", $"Remove \"{site.DisplayLabel}\"? You'll need to sign in again if you add it back.",
            "Remove", "Cancel");
        if (!confirmed) return;

        await profileService.DeleteProfileAsync(site.Id);
        Sites.Remove(site);
    }
}
