using BlogIt.MauiAdmin.Messages;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace BlogIt.MauiAdmin.ViewModels.Account;

public partial class AccountViewModel(MauiApiClient apiClient, SiteProfileService profileService) : ObservableObject
{
    [ObservableProperty] private string? username;
    [ObservableProperty] private string? displayName;
    [ObservableProperty] private string currentPassword = string.Empty;
    [ObservableProperty] private string newPassword = string.Empty;
    [ObservableProperty] private string confirmPassword = string.Empty;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isBusy;

    [RelayCommand]
    public async Task LoadAsync()
    {
        var activeSite = await profileService.GetActiveProfileAsync();
        Username = activeSite?.Username;
        DisplayName = activeSite?.DisplayName;
    }

    [RelayCommand]
    private async Task ChangePasswordAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;

        if (string.IsNullOrWhiteSpace(CurrentPassword) || string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "All fields are required.";
            return;
        }
        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "New password and confirmation don't match.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await apiClient.ChangePasswordAsync(new ChangePasswordRequest(CurrentPassword, NewPassword));
            if (!result.Success)
            {
                // Fixed: the reference admin discards this response body and shows a
                // generic HTTP-status message instead of the real server text (e.g.
                // "Current password is incorrect.").
                ErrorMessage = result.Error!.Message;
                return;
            }

            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            StatusMessage = "Password changed. Sign in again with your new password.";

            // The server moved this account's security stamp, so the token in hand is already
            // dead. ActiveSiteHttpMessageHandler would catch the 401 on the next request anyway;
            // clearing here means the user isn't shown a working-looking screen until then.
            var profile = await profileService.GetActiveProfileAsync();
            if (profile is not null)
            {
                await profileService.ClearTokenAsync(profile.Id);
                WeakReferenceMessenger.Default.Send(new SiteAuthExpiredMessage(profile.Id));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
