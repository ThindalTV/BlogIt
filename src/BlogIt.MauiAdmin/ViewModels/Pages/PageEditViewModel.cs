using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Pages;

public partial class PageEditViewModel(MauiApiClient apiClient, SiteProfileService profileService, IDialogService dialogService)
    : ObservableObject, IQueryAttributable
{
    private Guid? _id;

    // The concurrency token from the load this edit is based on — see PostEditViewModel.
    private Guid _concurrencyStamp;
    private bool _wasPublishedOnLoad;

    [ObservableProperty] private string pageTitle = "New Page";
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string slug = string.Empty;
    [ObservableProperty] private bool slugLocked;
    [ObservableProperty] private string content = string.Empty;
    [ObservableProperty] private string? seoTitle;
    [ObservableProperty] private string? seoDescription;
    [ObservableProperty] private string? seoKeywords;
    [ObservableProperty] private string? ogImageUrl;
    [ObservableProperty] private bool isPublished;

    [ObservableProperty] private bool schedulePublishEnabled;
    [ObservableProperty] private DateTime schedulePublishDate = DateTime.Today;
    [ObservableProperty] private TimeSpan schedulePublishTime = DateTime.Now.TimeOfDay;

    [ObservableProperty] private bool scheduleUnpublishEnabled;
    [ObservableProperty] private DateTime scheduleUnpublishDate = DateTime.Today;
    [ObservableProperty] private TimeSpan scheduleUnpublishTime = DateTime.Now.TimeOfDay;

    [ObservableProperty] private string? errorMessage;
    [ObservableProperty] private string? statusMessage;
    [ObservableProperty] private bool isBusy;

    public bool IsNew => _id is null;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var idObj) && idObj is string idStr && Guid.TryParse(idStr, out var id))
            _ = LoadAsync(id);
    }

    private async Task LoadAsync(Guid id)
    {
        IsBusy = true;
        try
        {
            var result = await apiClient.GetPageAsync(id);
            if (!result.Success) { ErrorMessage = result.Error!.Message; return; }

            var page = result.Value!;
            _id = page.Id;
            _concurrencyStamp = page.ConcurrencyStamp;
            PageTitle = "Edit Page";
            Title = page.Title;
            Slug = page.Slug;
            SlugLocked = page.HasBeenPublished;
            Content = page.Content;
            SeoTitle = page.SeoTitle;
            SeoDescription = page.SeoDescription;
            SeoKeywords = page.SeoKeywords;
            OgImageUrl = page.OgImageUrl;
            IsPublished = page.IsPublished;
            _wasPublishedOnLoad = page.IsPublished;

            if (page.ScheduledPublishAt is { } spa)
            {
                SchedulePublishEnabled = true;
                var local = DateTime.SpecifyKind(spa, DateTimeKind.Utc).ToLocalTime();
                SchedulePublishDate = local.Date;
                SchedulePublishTime = local.TimeOfDay;
            }
            if (page.ScheduledUnpublishAt is { } sua)
            {
                ScheduleUnpublishEnabled = true;
                var local = DateTime.SpecifyKind(sua, DateTimeKind.Utc).ToLocalTime();
                ScheduleUnpublishDate = local.Date;
                ScheduleUnpublishTime = local.TimeOfDay;
            }

            OnPropertyChanged(nameof(IsNew));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static DateTime? CombineToUtc(bool enabled, DateTime date, TimeSpan time)
    {
        if (!enabled) return null;
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Local);
        return local.ToUniversalTime();
    }

    private bool Validate()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Title is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(Slug))
        {
            ErrorMessage = "Slug is required.";
            return false;
        }

        var publishAt = CombineToUtc(SchedulePublishEnabled, SchedulePublishDate, SchedulePublishTime);
        var unpublishAt = CombineToUtc(ScheduleUnpublishEnabled, ScheduleUnpublishDate, ScheduleUnpublishTime);
        var scheduleError = PublicationSchedule.Validate(publishAt, unpublishAt);
        if (scheduleError is not null)
        {
            ErrorMessage = scheduleError;
            return false;
        }

        return true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate()) return;

        // Pages fold "published" into a single field rather than separate
        // publish/unpublish actions, so the equivalent safety net here is a
        // confirmation specifically when Save is about to take an already-live
        // page offline.
        if (_wasPublishedOnLoad && !IsPublished)
        {
            var confirmed = await dialogService.ConfirmAsync(
                "Unpublish page", "This page will no longer be visible on your site. Continue?", "Unpublish", "Cancel");
            if (!confirmed) return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var publishAt = CombineToUtc(SchedulePublishEnabled, SchedulePublishDate, SchedulePublishTime);
            var unpublishAt = CombineToUtc(ScheduleUnpublishEnabled, ScheduleUnpublishDate, ScheduleUnpublishTime);

            if (_id is null)
            {
                var request = new CreatePageRequest(Title, Slug, Content, SeoTitle, SeoDescription, SeoKeywords, OgImageUrl, IsPublished, publishAt, unpublishAt);
                var result = await apiClient.CreatePageAsync(request);
                if (!result.Success) { ErrorMessage = result.Error!.Message; return; }
                ApplyServerState(result.Value!);
            }
            else
            {
                var request = new UpdatePageRequest(Title, Slug, Content, SeoTitle, SeoDescription, SeoKeywords, OgImageUrl, IsPublished, publishAt, unpublishAt, _concurrencyStamp);
                var result = await apiClient.UpdatePageAsync(_id.Value, request);
                if (!result.Success) { ErrorMessage = result.Error!.Message; return; }
                ApplyServerState(result.Value!);
            }

            StatusMessage = "Saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyServerState(PageDto page)
    {
        _id = page.Id;
        _concurrencyStamp = page.ConcurrencyStamp;
        Slug = page.Slug;
        SlugLocked = page.HasBeenPublished;
        IsPublished = page.IsPublished;
        _wasPublishedOnLoad = page.IsPublished;
        PageTitle = "Edit Page";
        OnPropertyChanged(nameof(IsNew));
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (_id is null)
        {
            await dialogService.AlertAsync("Save first", "Save this page before previewing it.");
            return;
        }

        var result = await apiClient.CreatePagePreviewAsync(_id.Value);
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
    private async Task DeleteAsync()
    {
        if (_id is null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        var confirmed = await dialogService.ConfirmAsync("Delete page", $"Delete \"{Title}\"? This can't be undone.", "Delete", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.DeletePageAsync(_id.Value);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't delete", result.Error!.Message);
            return;
        }
        await Shell.Current.GoToAsync("..");
    }

    /// <summary>Inserts a Markdown reference to the given media item at the given
    /// cursor position (relative paths, matching how the server-rendered content
    /// resolves media links against the public site root).</summary>
    public void InsertMediaMarkdown(MediaFileDto media, int cursorPosition)
    {
        var isImage = media.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        var markdown = isImage ? $"![{media.Title}]({media.PublicPath})" : $"[{media.Title}]({media.PublicPath})";

        var pos = Math.Clamp(cursorPosition, 0, Content.Length);
        Content = Content.Insert(pos, markdown);
    }
}
