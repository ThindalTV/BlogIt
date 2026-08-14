using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Sites;

public partial class LoginViewModel(SiteProfileService profileService, MauiApiClient apiClient)
    : ObservableObject, IQueryAttributable
{
    private string _siteId = string.Empty;

    [ObservableProperty]
    private string siteLabel = "this site";

    [ObservableProperty]
    private string username = string.Empty;

    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string? errorMessage;

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
        SiteLabel = profiles.FirstOrDefault(p => p.Id == id)?.DisplayLabel ?? "this site";
    }

    [RelayCommand]
    private async Task SignInAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Enter your username and password.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            // No refresh-token mechanism exists server-side, and we deliberately never
            // cache the raw password — re-entering credentials on expiry is normal,
            // acceptable mobile-app UX here, not a shortcut we're skipping.
            var result = await apiClient.LoginAsync(_siteId, Username, Password);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            await profileService.SetActiveAsync(_siteId);
            Password = string.Empty;
            await Shell.Current.GoToAsync("//dashboard");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
