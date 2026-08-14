using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
            StatusMessage = "Password changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
