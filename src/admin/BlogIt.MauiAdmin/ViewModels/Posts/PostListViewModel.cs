using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Core.Publishing;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Posts;

/// <summary>Display wrapper pairing a post with its one-line publication status. The status is
/// computed once here rather than in a binding converter because it depends on three of the
/// DTO's fields at once, and the list previously showed only Published/Draft — hiding every
/// scheduled post behind a label that made it look like an abandoned draft.</summary>
public record PostRow(BlogPostSummaryDto Dto)
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

public partial class PostListViewModel(MauiApiClient apiClient, SiteProfileService profileService, IDialogService dialogService)
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

    public ObservableCollection<PostRow> Posts { get; } = [];

    /// <summary>
    /// Reload when the filter changes. Without this the Picker set the property and nothing
    /// else happened, so the filter appeared inert until the search box was re-submitted.
    /// </summary>
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
            var result = await apiClient.GetPostsAsync(SearchText, Page, PageSize, StatusFilter);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            Posts.Clear();
            foreach (var post in result.Value!.Items)
                Posts.Add(new PostRow(post));
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
    private async Task NewPostAsync() => await Shell.Current.GoToAsync("posts/new");

    [RelayCommand]
    private async Task EditAsync(PostRow post) => await Shell.Current.GoToAsync($"posts/edit?id={post.Id}");

    [RelayCommand]
    private async Task PreviewAsync(PostRow post)
    {
        var result = await apiClient.CreatePostPreviewAsync(post.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Preview unavailable", result.Error!.Message);
            return;
        }

        var activeSite = await profileService.GetActiveProfileAsync();
        if (activeSite is null) return;

        var previewUri = new Uri(activeSite.BaseUri, result.Value!.Url);
        await Browser.Default.OpenAsync(previewUri, BrowserLaunchMode.SystemPreferred);
    }

    [RelayCommand]
    private async Task PublishAsync(PostRow post)
    {
        var result = await apiClient.PublishPostAsync(post.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't publish", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task UnpublishAsync(PostRow post)
    {
        // Unpublishing a live post is never a side effect of another action — it
        // always requires its own explicit confirmation.
        var confirmed = await dialogService.ConfirmAsync(
            "Unpublish post", $"\"{post.Title}\" will no longer be visible on your site. Continue?",
            "Unpublish", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.UnpublishPostAsync(post.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't unpublish", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task CancelScheduleAsync(PostRow post)
    {
        var result = await apiClient.UpdatePostScheduleAsync(post.Id, new(null, null));
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't cancel schedule", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(PostRow post)
    {
        var confirmed = await dialogService.ConfirmAsync("Delete post", $"Delete \"{post.Title}\"? This can't be undone.", "Delete", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.DeletePostAsync(post.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't delete", result.Error!.Message);
            return;
        }
        await LoadAsync();
    }
}
