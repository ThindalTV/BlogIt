using Microsoft.Maui.Media;
using BlogIt.MauiAdmin.Models;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// Provides camera capture, photo library access, and file picking.
/// Returns a stream + metadata ready for upload to the media API.
/// </summary>
public class MediaCaptureService
{
    public record CapturedMedia(Stream Data, string FileName, string ContentType);

    /// <summary>Opens the device camera to capture a new photo.</summary>
    public async Task<CapturedMedia?> CapturePhotoAsync()
    {
        if (!MediaPicker.Default.IsCaptureSupported)
            return null;

        var photo = await MediaPicker.Default.CapturePhotoAsync();
        return await ToMediaAsync(photo);
    }

    /// <summary>Opens the photo library for the user to pick an image.</summary>
    public async Task<CapturedMedia?> PickPhotoAsync()
    {
        var photo = await MediaPicker.Default.PickPhotoAsync();
        return await ToMediaAsync(photo);
    }

    /// <summary>Opens the file picker for any file type.</summary>
    public async Task<CapturedMedia?> PickFileAsync()
    {
        var result = await FilePicker.Default.PickAsync(PickOptions.Default);
        if (result is null) return null;

        var stream = await result.OpenReadAsync();
        var contentType = GetContentType(result.FileName);
        return new CapturedMedia(stream, result.FileName, contentType);
    }

    /// <summary>Pick multiple files at once (e.g. bulk media upload).</summary>
    public async Task<List<CapturedMedia>> PickMultipleFilesAsync()
    {
        var results = await FilePicker.Default.PickMultipleAsync(PickOptions.Default);
        var media = new List<CapturedMedia>();
        foreach (var result in results)
        {
            var stream = await result.OpenReadAsync();
            media.Add(new CapturedMedia(stream, result.FileName, GetContentType(result.FileName)));
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

    private static string GetContentType(string fileName)
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
            ".pdf" => "application/pdf",
            ".doc" or ".docx" => "application/msword",
            _ => "application/octet-stream"
        };
    }
}
