using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Pages;

public partial class PageListViewModel(MauiApiClient apiClient, SiteProfileService profileService, IDialogService dialogService)
    : ObservableObject
{
    private const int PageSize = 20;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private int page = 1;

    [ObservableProperty]
    private int totalCount;

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public ObservableCollection<PageDto> Pages { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            // Real pagination — the reference Blazor admin has none for Pages at all
            // and silently caps at the server's default 20-item page.
            var result = await apiClient.GetPagesAsync(SearchText, Page, PageSize);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            Pages.Clear();
            foreach (var p in result.Value!.Items)
                Pages.Add(p);
            TotalCount = result.Value.TotalCount;
            OnPropertyChanged(nameof(TotalPages));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        Page = 1;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (Page >= TotalPages) return;
        Page++;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (Page <= 1) return;
        Page--;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NewPageAsync() => await Shell.Current.GoToAsync("pages/new");

    [RelayCommand]
    private async Task EditAsync(PageDto page) => await Shell.Current.GoToAsync($"pages/edit?id={page.Id}");

    [RelayCommand]
    private async Task PreviewAsync(PageDto page)
    {
        var result = await apiClient.CreatePagePreviewAsync(page.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Preview unavailable", result.Error!.Message);
            return;
        }

        var activeSite = await profileService.GetActiveProfileAsync();
        if (activeSite is null) return;
        await Browser.Default.OpenAsync(new Uri(activeSite.BaseUri, result.Value!.Url), BrowserLaunchMode.SystemPreferred);
    }

    [RelayCommand]
    private async Task DeleteAsync(PageDto page)
    {
        var confirmed = await dialogService.ConfirmAsync("Delete page", $"Delete \"{page.Title}\"? This can't be undone.", "Delete", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.DeletePageAsync(page.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't delete", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }
}
