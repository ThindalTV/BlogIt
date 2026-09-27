namespace BlogIt.MauiAdmin.Core.Media;

/// <summary>
/// Client-side extension allow-list + size cap applied before every upload. The
/// server caps size (BlogItOptions.MaxMediaUploadBytes, 50 MB by default) but
/// deliberately does not restrict type: it serves back whatever Content-Type the
/// client reported, because administrators are trusted (see the accepted risks in
/// docs/history/AUDIT_REPORT_2026-08-14.md). The allow-list here is the only type
/// check an upload gets; the size cap just fails fast before a doomed upload.
/// </summary>
public static class MediaUploadPolicy
{
    public const long MaxSizeBytes = 50 * 1024 * 1024; // 50MB, matching the reference admin's own hint

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".heic", ".heif",
        ".mp4", ".mov", ".webm", ".avi", ".mkv", ".3gp", ".m4v",
        ".pdf"
    };

    /// <summary>Returns a user-facing rejection reason, or null if the file passes.</summary>
    public static string? Validate(string fileName, long sizeBytes)
    {
        var ext = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(ext))
            return $"\"{ext}\" files aren't allowed. Choose an image, video, or PDF.";

        if (sizeBytes > MaxSizeBytes)
            return $"File is too large ({sizeBytes / (1024 * 1024)} MB). The limit is {MaxSizeBytes / (1024 * 1024)} MB.";

        return null;
    }
}
