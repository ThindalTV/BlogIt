using System.Collections.ObjectModel;
using BlogIt.MauiAdmin.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BlogIt.MauiAdmin.ViewModels.Media;

/// <summary>
/// Backs the Insert-Media modal used by the Post/Page editors: browse the existing
/// library, or capture/upload something new, then hand the chosen item back to the
/// caller. Deliberately separate from MediaListViewModel — this is a much smaller
/// read-mostly "pick one" surface, not the full library management screen.
/// </summary>
public partial class MediaPickerViewModel(
    MauiApiClient apiClient,
    SiteProfileService profileService,
    IMediaCaptureService captureService,
    IDialogService dialogService) : ObservableObject
{
    private const int MaxItems = 50;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public bool IsCaptureSupported => captureService.IsCaptureSupported;

    public ObservableCollection<MediaItemRow> Items { get; } = [];

    /// <summary>Raised when the user picks (or just uploaded) an item to insert.</summary>
    public event Action<MediaItemRow>? ItemSelected;

    [RelayCommand]
    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var activeSite = await profileService.GetActiveProfileAsync();
            var result = await apiClient.GetMediaAsync(SearchText, 1, MaxItems);
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
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync() => await LoadAsync();

    [RelayCommand]
    private void Select(MediaItemRow item) => ItemSelected?.Invoke(item);

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

            // Upload and immediately use it — no need to make the user find their
            // own upload in the grid right after adding it.
            var activeSite = await profileService.GetActiveProfileAsync();
            var url = activeSite is null ? result.Value!.PublicPath : new Uri(activeSite.BaseUri, result.Value!.PublicPath).ToString();
            ItemSelected?.Invoke(new MediaItemRow(result.Value!, url));
        }
        finally
        {
            IsBusy = false;
        }
    }
}
