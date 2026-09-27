using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Users;

/// <summary>Display wrapper flagging whether a row is the signed-in user, so the
/// delete affordance can be hidden for it directly via a bindable property instead
/// of a fragile cross-item XAML comparison. The server independently rejects
/// self-delete too (defense in depth), so hiding the button here is a UX nicety,
/// not the only safeguard.</summary>
public record UserRow(AppUserDto Dto, bool IsCurrentUser)
{
    public Guid Id => Dto.Id;
    public string Username => Dto.Username;
    public string DisplayName => Dto.DisplayName;
}

public partial class UserListViewModel(MauiApiClient apiClient, SiteProfileService profileService, IDialogService dialogService)
    : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? currentUsername;

    [ObservableProperty]
    private bool showNewUserForm;

    [ObservableProperty]
    private string newUsername = string.Empty;

    [ObservableProperty]
    private string newDisplayName = string.Empty;

    [ObservableProperty]
    private string newPassword = string.Empty;

    public ObservableCollection<UserRow> Users { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var activeSite = await profileService.GetActiveProfileAsync();
            CurrentUsername = activeSite?.Username;

            // Server doesn't paginate this endpoint at all — a flat list matches it.
            var result = await apiClient.GetUsersAsync();
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            Users.Clear();
            foreach (var u in result.Value!)
                Users.Add(new UserRow(u, string.Equals(u.Username, CurrentUsername, StringComparison.Ordinal)));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleNewUserForm() => ShowNewUserForm = !ShowNewUserForm;

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(NewUsername) || string.IsNullOrWhiteSpace(NewPassword))
        {
            ErrorMessage = "Username and password are required.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var request = new CreateUserRequest(NewUsername.Trim(), NewDisplayName.Trim(), NewPassword);
            var result = await apiClient.CreateUserAsync(request);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            NewUsername = string.Empty;
            NewDisplayName = string.Empty;
            NewPassword = string.Empty;
            ShowNewUserForm = false;
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(UserRow user)
    {
        var confirmed = await dialogService.ConfirmAsync("Delete user", $"Delete \"{user.Username}\"? This can't be undone.", "Delete", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.DeleteUserAsync(user.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't delete", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }
}
