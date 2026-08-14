using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Services;
using BlogIt.Shared.DTOs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace BlogIt.MauiAdmin.ViewModels.Media;

/// <summary>Display wrapper pairing a MediaFileDto with the absolute URL needed to
/// actually render its thumbnail — MediaFileDto.PublicPath is server-relative, and
/// resolving it against the active site happens once here rather than per-binding.</summary>
public record MediaItemRow(MediaFileDto Dto, string ImageUrl)
{
    public Guid Id => Dto.Id;
    public string Title => Dto.Title;
    public string FileName => Dto.FileName;
    public bool IsImage => Dto.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

public partial class MediaListViewModel(
    MauiApiClient apiClient,
    SiteProfileService profileService,
    IMediaCaptureService captureService,
    IDialogService dialogService) : ObservableObject
{
    private const int PageSize = 20;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private int page = 1;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private MediaItemRow? selectedItem;

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

    public bool IsCaptureSupported => captureService.IsCaptureSupported;

    public ObservableCollection<MediaItemRow> Items { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var activeSite = await profileService.GetActiveProfileAsync();

            // Real pagination — the reference Blazor admin has none for Media at
            // all and silently caps at the server's default 20-item page.
            var result = await apiClient.GetMediaAsync(SearchText, Page, PageSize);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            Items.Clear();
            foreach (var item in result.Value!.Items)
            {
                var url = activeSite is null ? item.PublicPath : new Uri(activeSite.BaseUri, item.PublicPath).ToString();
                Items.Add(new MediaItemRow(item, url));
            }
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
    private void Select(MediaItemRow item) => SelectedItem = item;

    [RelayCommand]
    private async Task CopyUrlAsync(MediaItemRow item)
    {
        await Clipboard.Default.SetTextAsync(item.ImageUrl);
        StatusMessage = "URL copied to clipboard.";
    }

    [RelayCommand]
    private async Task DeleteAsync(MediaItemRow item)
    {
        var confirmed = await dialogService.ConfirmAsync("Delete media", $"Delete \"{item.Title}\"? This can't be undone.", "Delete", "Cancel");
        if (!confirmed) return;

        var result = await apiClient.DeleteMediaAsync(item.Id);
        if (!result.Success)
        {
            await dialogService.AlertAsync("Couldn't delete", result.Error!.Message);
            return;
        }

        if (SelectedItem?.Id == item.Id) SelectedItem = null;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task TakePhotoAsync() => await CaptureAndUploadAsync(captureService.CapturePhotoAsync);

    [RelayCommand]
    private async Task RecordVideoAsync() => await CaptureAndUploadAsync(captureService.CaptureVideoAsync);

    [RelayCommand]
    private async Task ChoosePhotoAsync() => await CaptureAndUploadAsync(captureService.PickPhotoAsync);

    [RelayCommand]
    private async Task ChooseVideoAsync() => await CaptureAndUploadAsync(captureService.PickVideoAsync);

    [RelayCommand]
    private async Task ChooseFileAsync() => await CaptureAndUploadAsync(captureService.PickFileAsync);

    private async Task CaptureAndUploadAsync(Func<Task<CapturedMedia?>> capture)
    {
        var media = await capture();
        if (media is null) return;

        await using var _ = media.Data;

        var policyError = MediaUploadPolicy.Validate(media.FileName, media.Data.CanSeek ? media.Data.Length : 0);
        if (policyError is not null)
        {
            await dialogService.AlertAsync("Can't upload this file", policyError);
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var title = Path.GetFileNameWithoutExtension(media.FileName);
            var result = await apiClient.UploadMediaAsync(title, media.Data, media.FileName, media.ContentType);
            if (!result.Success)
            {
                ErrorMessage = result.Error!.Message;
                return;
            }

            StatusMessage = "Uploaded.";
            Page = 1;
            await LoadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
