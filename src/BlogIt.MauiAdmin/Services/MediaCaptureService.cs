using Microsoft.Maui.Media;

namespace BlogIt.MauiAdmin.Services;

/// <summary>Real device implementation of <see cref="IMediaCaptureService"/>, backed
/// by MAUI's MediaPicker/FilePicker.</summary>
public class MediaCaptureService : IMediaCaptureService
{
    public bool IsCaptureSupported => MediaPicker.Default.IsCaptureSupported;

    /// <summary>Opens the device camera to capture a new photo.</summary>
    public async Task<CapturedMedia?> CapturePhotoAsync()
    {
        if (!IsCaptureSupported) return null;
        var photo = await MediaPicker.Default.CapturePhotoAsync();
        return await ToMediaAsync(photo);
    }

    /// <summary>Opens the device camera to record a new video.</summary>
    public async Task<CapturedMedia?> CaptureVideoAsync()
    {
        if (!IsCaptureSupported) return null;
        var video = await MediaPicker.Default.CaptureVideoAsync();
        return await ToMediaAsync(video);
    }

    /// <summary>Opens the photo library for the user to pick a single image.</summary>
    public async Task<CapturedMedia?> PickPhotoAsync()
    {
        var photos = await MediaPicker.Default.PickPhotosAsync();
        return await ToMediaAsync(photos.FirstOrDefault());
    }

    /// <summary>Opens the video library for the user to pick a single video.</summary>
    public async Task<CapturedMedia?> PickVideoAsync()
    {
        var videos = await MediaPicker.Default.PickVideosAsync();
        return await ToMediaAsync(videos.FirstOrDefault());
    }

    /// <summary>Opens the file picker for any file type.</summary>
    public async Task<CapturedMedia?> PickFileAsync()
    {
        var result = await FilePicker.Default.PickAsync(PickOptions.Default);
        return await ToMediaAsync(result);
    }

    /// <summary>Pick multiple files at once (e.g. bulk media upload).</summary>
    public async Task<List<CapturedMedia>> PickMultipleFilesAsync()
    {
        var results = await FilePicker.Default.PickMultipleAsync(PickOptions.Default);
        var media = new List<CapturedMedia>();
        foreach (var result in results)
        {
            var captured = await ToMediaAsync(result);
            if (captured is not null)
                media.Add(captured);
        }
        return media;
    }

    private static async Task<CapturedMedia?> ToMediaAsync(FileResult? result)
    {
        if (result is null) return null;
        var stream = await result.OpenReadAsync();
        var contentType = GetContentType(result.FileName);
        return new CapturedMedia(stream, result.FileName, contentType);
    }

    /// <summary>Always derives its own Content-Type from the file extension rather
    /// than trusting a platform-supplied one, for deterministic behavior across
    /// platforms. The server trusts whatever Content-Type it's sent with no
    /// validation of its own (confirmed critical finding in AUDIT_REPORT.md), so
    /// this mapping is the only thing keeping uploads honest.</summary>
    internal static string GetContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".heic" or ".heif" => "image/heic",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mkv" => "video/x-matroska",
            ".3gp" => "video/3gpp",
            ".m4v" => "video/x-m4v",
            ".pdf" => "application/pdf",
            ".doc" or ".docx" => "application/msword",
            _ => "application/octet-stream"
        };
    }
}
