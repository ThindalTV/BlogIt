namespace BlogIt.MauiAdmin.Services;

public record CapturedMedia(Stream Data, string FileName, string ContentType);

/// <summary>
/// Provides camera photo/video capture, photo/video library access, and file picking.
/// Returns a stream + metadata ready for upload to the media API. Every capture
/// button in the UI should check <see cref="IsCaptureSupported"/> at runtime and
/// hide/disable itself if false, rather than excluding it at compile time per
/// platform — this matters most for a desktop/VM with no webcam.
/// </summary>
public interface IMediaCaptureService
{
    bool IsCaptureSupported { get; }

    Task<CapturedMedia?> CapturePhotoAsync();
    Task<CapturedMedia?> CaptureVideoAsync();
    Task<CapturedMedia?> PickPhotoAsync();
    Task<CapturedMedia?> PickVideoAsync();
    Task<CapturedMedia?> PickFileAsync();
    Task<List<CapturedMedia>> PickMultipleFilesAsync();
}
