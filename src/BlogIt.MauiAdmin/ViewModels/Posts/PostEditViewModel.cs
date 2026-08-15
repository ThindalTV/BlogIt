using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Posts;

public partial class PostEditViewModel(MauiApiClient apiClient, SiteProfileService profileService, IDialogService dialogService)
    : ObservableObject, IQueryAttributable
{
    private Guid? _id;

    // The concurrency token from the load this edit is based on. Sent back on update so the server
    // can reject an edit that would overwrite someone else's newer save, and refreshed from every
    // response that carries a newer one.
    private Guid _concurrencyStamp;

    [ObservableProperty] private string pageTitle = "New Post";
    [ObservableProperty] private string title = string.Empty;
    [ObservableProperty] private string? slug;
    [ObservableProperty] private bool slugLocked;
    [ObservableProperty] private string summary = string.Empty;
    [ObservableProperty] private string content = string.Empty;
    [ObservableProperty] private string tagsText = string.Empty;
    [ObservableProperty] private string? seoTitle;
    [ObservableProperty] private string? seoDescription;
    [ObservableProperty] private string? seoKeywords;
    [ObservableProperty] private string? ogImageUrl;

    [ObservableProperty] private bool isPublished;
    [ObservableProperty] private bool hasBeenPublished;

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
            var result = await apiClient.GetPostAsync(id);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            var post = result.Value!;
            _id = post.Id;
            _concurrencyStamp = post.ConcurrencyStamp;
            PageTitle = "Edit Post";
            Title = post.Title;
            Slug = post.Slug;
            SlugLocked = post.HasBeenPublished;
            Summary = post.Summary;
            Content = post.Content ?? string.Empty;
            TagsText = string.Join(", ", post.Tags.Select(t => t.Name));
            SeoTitle = post.SeoTitle;
            SeoDescription = post.SeoDescription;
            SeoKeywords = post.SeoKeywords;
            OgImageUrl = post.OgImageUrl;
            IsPublished = post.IsPublished;
            HasBeenPublished = post.HasBeenPublished;

            if (post.ScheduledPublishAt is { } spa)
            {
                SchedulePublishEnabled = true;
                var local = DateTime.SpecifyKind(spa, DateTimeKind.Utc).ToLocalTime();
                SchedulePublishDate = local.Date;
                SchedulePublishTime = local.TimeOfDay;
            }
            if (post.ScheduledUnpublishAt is { } sua)
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

    private List<string> GetTags() =>
        [.. TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private bool Validate()
    {
        ErrorMessage = null;
        if (string.IsNullOrWhiteSpace(Title))
        {
            ErrorMessage = "Title is required.";
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

    private void ApplyServerState(BlogPostDetailDto post)
    {
        _id = post.Id;
        // Every mutating response carries the post's new token; picking it up here is what lets a
        // user save twice in a row without a spurious conflict on the second save.
        _concurrencyStamp = post.ConcurrencyStamp;
        Slug = post.Slug;
        SlugLocked = post.HasBeenPublished;
        IsPublished = post.IsPublished;
        HasBeenPublished = post.HasBeenPublished;
        PageTitle = "Edit Post";
        OnPropertyChanged(nameof(IsNew));
    }

    private async Task<bool> PersistAsync()
    {
        StatusMessage = null;
        var publishAt = CombineToUtc(SchedulePublishEnabled, SchedulePublishDate, SchedulePublishTime);
        var unpublishAt = CombineToUtc(ScheduleUnpublishEnabled, ScheduleUnpublishDate, ScheduleUnpublishTime);
        var tags = GetTags();

        if (_id is null)
        {
            var request = new CreateBlogPostRequest(Title, Summary, Content, SeoTitle, SeoDescription, SeoKeywords, OgImageUrl, tags, publishAt, unpublishAt, Slug);
            var result = await apiClient.CreatePostAsync(request);
            if (!result.Success) { ErrorMessage = result.Error!.Message; return false; }
            ApplyServerState(result.Value!);
        }
        else
        {
            var request = new UpdateBlogPostRequest(Title, Summary, Content, SeoTitle, SeoDescription, SeoKeywords, OgImageUrl, tags, publishAt, unpublishAt, Slug, _concurrencyStamp);
            var result = await apiClient.UpdatePostAsync(_id.Value, request);
            if (!result.Success) { ErrorMessage = result.Error!.Message; return false; }
            ApplyServerState(result.Value!);
        }

        return true;
    }

    /// <summary>Persists content only — never touches publish state. Publishing and
    /// unpublishing are separate, distinctly-labeled commands below, by design: this
    /// button can never silently take a live post offline.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate()) return;
        IsBusy = true;
        try
        {
            if (await PersistAsync())
                StatusMessage = "Saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PublishAsync()
    {
        if (!Validate()) return;
        IsBusy = true;
        try
        {
            if (!await PersistAsync()) return;

            var result = await apiClient.PublishPostAsync(_id!.Value);
            if (!result.Success) { ErrorMessage = result.Error!.Message; return; }
            ApplyServerState(result.Value!);
            StatusMessage = "Published.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnpublishAsync()
    {
        if (_id is null) return;

        var confirmed = await dialogService.ConfirmAsync(
            "Unpublish post", "This post will no longer be visible on your site. Continue?", "Unpublish", "Cancel");
        if (!confirmed) return;

        IsBusy = true;
        try
        {
            var result = await apiClient.UnpublishPostAsync(_id.Value);
            if (!result.Success) { ErrorMessage = result.Error!.Message; return; }
            ApplyServerState(result.Value!);
            StatusMessage = "Unpublished.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelScheduleAsync()
    {
        if (_id is null) return;

        var result = await apiClient.UpdatePostScheduleAsync(_id.Value, new(null, null));
        if (!result.Success) { ErrorMessage = result.Error!.Message; return; }

        ApplyServerState(result.Value!);
        SchedulePublishEnabled = false;
        ScheduleUnpublishEnabled = false;
        StatusMessage = "Schedule cleared.";
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (_id is null)
        {
            await dialogService.AlertAsync("Save first", "Save this post before previewing it.");
            return;
        }

        var result = await apiClient.CreatePostPreviewAsync(_id.Value);
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

        var confirmed = await dialogService.ConfirmAsync("Delete post", $"Delete \"{Title}\"? This can't be undone.", "Delete", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.DeletePostAsync(_id.Value);
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
