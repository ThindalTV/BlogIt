using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Redirects;

public partial class RedirectListViewModel(MauiApiClient apiClient, IDialogService dialogService) : ObservableObject
{
    private Guid? _editingId;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private bool showEditor;
    [ObservableProperty] private string editorTitle = "New Redirect";
    [ObservableProperty] private string sourcePath = string.Empty;
    [ObservableProperty] private string targetUrl = string.Empty;
    [ObservableProperty] private bool isPermanent;

    public ObservableCollection<UrlRedirectDto> Redirects { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await apiClient.GetRedirectsAsync();
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            Redirects.Clear();
            foreach (var r in result.Value!.OrderBy(r => r.SourcePath))
                Redirects.Add(r);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewRedirect()
    {
        _editingId = null;
        EditorTitle = "New Redirect";
        SourcePath = string.Empty;
        TargetUrl = string.Empty;
        IsPermanent = false;
        ShowEditor = true;
    }

    [RelayCommand]
    private void Edit(UrlRedirectDto redirect)
    {
        _editingId = redirect.Id;
        EditorTitle = "Edit Redirect";
        SourcePath = redirect.SourcePath;
        TargetUrl = redirect.TargetUrl;
        IsPermanent = redirect.IsPermanent;
        ShowEditor = true;
    }

    [RelayCommand]
    private void CancelEdit() => ShowEditor = false;

    /// <summary>Mirrors the server's RedirectPathValidator for a good in-app error
    /// experience before the round trip — the server still enforces the real rules.</summary>
    private string? ValidateLocally()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || !SourcePath.StartsWith('/'))
            return "Source must start with \"/\".";
        if (SourcePath.StartsWith("//", StringComparison.Ordinal))
            return "Source can't be protocol-relative.";
        if (SourcePath.Length > 1000)
            return "Source is too long.";
        if (SourcePath.Any(char.IsWhiteSpace) || SourcePath.Contains('?') || SourcePath.Contains('#'))
            return "Source can't contain whitespace, \"?\", or \"#\".";
        if (string.IsNullOrWhiteSpace(TargetUrl))
            return "Target is required.";
        if (TargetUrl.Length > 2000)
            return "Target is too long.";
        if (TargetUrl.StartsWith("//", StringComparison.Ordinal))
            return "Target can't be protocol-relative.";
        var isExternal = TargetUrl.Contains("://", StringComparison.Ordinal);
        if (isExternal && !TargetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !TargetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return "External targets must use http or https.";
        if (SourcePath == TargetUrl)
            return "Source and target can't be the same.";

        return null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var validationError = ValidateLocally();
        if (validationError is not null)
        {
            ErrorMessage = validationError;
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var request = new SaveUrlRedirectRequest(SourcePath.Trim(), TargetUrl.Trim(), IsPermanent);
            var result = _editingId is null
                ? await apiClient.CreateRedirectAsync(request)
                : await apiClient.UpdateRedirectAsync(_editingId.Value, request);

            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            ShowEditor = false;
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(UrlRedirectDto redirect)
    {
        // The reference Blazor admin is missing a confirmation here — every other
        // delete flow in the app has one, so this fixes that inconsistency.
        var confirmed = await dialogService.ConfirmAsync(
            "Delete redirect", $"Delete the redirect from \"{redirect.SourcePath}\"?", "Delete", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.DeleteRedirectAsync(redirect.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't delete", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }
}
