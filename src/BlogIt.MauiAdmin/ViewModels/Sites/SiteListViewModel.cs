using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Core.Sites;
using BlogIt.MauiAdmin.Models;
using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Sites;

public partial class SiteListViewModel(
    SiteProfileService profileService,
    SiteActivator activator,
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

    /// <summary>Shares its activation path with the site switcher — see <see cref="SiteActivator"/>
    /// for why that is one implementation and not two.</summary>
    [RelayCommand]
    private async Task ActivateAsync(SiteProfile site)
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await activator.ActivateAsync(site);
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
