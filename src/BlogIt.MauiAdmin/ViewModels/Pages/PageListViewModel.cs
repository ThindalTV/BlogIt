using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Core.Publishing;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Pages;

/// <summary>Display wrapper pairing a page with its one-line publication status — the same
/// treatment posts get, so a page queued for a launch date is not shown as a plain draft.</summary>
public record PageRow(PageDto Dto)
{
    public Guid Id => Dto.Id;
    public string Title => Dto.Title;
    public bool IsPublished => Dto.IsPublished;

    public bool IsScheduled => !Dto.IsPublished && Dto.ScheduledPublishAt.HasValue;

    public bool HasSchedule => Dto.ScheduledPublishAt.HasValue || Dto.ScheduledUnpublishAt.HasValue;

    public string StatusText => PublicationStatusText.Describe(
        Dto.IsPublished,
        PublicationStatusText.ToLocal(Dto.ScheduledPublishAt),
        PublicationStatusText.ToLocal(Dto.ScheduledUnpublishAt));
}

public partial class PageListViewModel(MauiApiClient apiClient, SiteProfileService profileService, IDialogService dialogService)
    : ObservableObject
{
    private const int PageSize = 20;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string statusFilter = "all";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private int page = 1;

    [ObservableProperty]
    private int totalCount;

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public ObservableCollection<PageRow> Pages { get; } = [];

    /// <summary>Reload when the filter changes — see PostListViewModel for why this is not
    /// left to the search box.</summary>
    partial void OnStatusFilterChanged(string value)
    {
        Page = 1;
        _ = LoadAsync();
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            // Real pagination — the reference Blazor admin has none for Pages at all
            // and silently caps at the server's default 20-item page.
            var result = await apiClient.GetPagesAsync(SearchText, Page, PageSize, StatusFilter);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            Pages.Clear();
            foreach (var p in result.Value!.Items)
                Pages.Add(new PageRow(p));
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
    private async Task EditAsync(PageRow page) => await Shell.Current.GoToAsync($"pages/edit?id={page.Id}");

    [RelayCommand]
    private async Task PreviewAsync(PageRow page)
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

    /// <summary>Pages have no dedicated publish endpoint — publication is a field on the page —
    /// so this is an update that changes only that field, leaving the rest as loaded.</summary>
    private async Task<bool> SetPublishedAsync(PageRow page, bool published)
    {
        var current = await apiClient.GetPageAsync(page.Id);
        if (!current.Success)
        {
            await dialogService.AlertAsync("Couldn't load page", current.Error!.Message);
            return false;
        }

        var dto = current.Value!;
        var request = new UpdatePageRequest(
            dto.Title, dto.Slug, dto.Content, dto.SeoTitle, dto.SeoDescription, dto.SeoKeywords,
            dto.OgImageUrl, published, dto.ScheduledPublishAt, dto.ScheduledUnpublishAt, dto.ConcurrencyStamp);

        var result = await apiClient.UpdatePageAsync(page.Id, request);
        if (!result.Success)
        {
            await dialogService.AlertAsync(published ? "Couldn't publish" : "Couldn't unpublish", result.Error!.Message);
            return false;
        }
        return true;
    }

    [RelayCommand]
    private async Task PublishAsync(PageRow page)
    {
        if (await SetPublishedAsync(page, true))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task UnpublishAsync(PageRow page)
    {
        // Taking a live page offline always asks first, exactly as the posts list does.
        var confirmed = await dialogService.ConfirmAsync(
            "Unpublish page", $"\"{page.Title}\" will no longer be visible on your site. Continue?",
            "Unpublish", "Cancel");
        if (!confirmed) return;

        if (await SetPublishedAsync(page, false))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task CancelScheduleAsync(PageRow page)
    {
        var result = await apiClient.UpdatePageScheduleAsync(page.Id, new(null, null));
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't cancel schedule", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(PageRow page)
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
