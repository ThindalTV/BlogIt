namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// Client-side extension allow-list + size cap applied before every upload. The
/// server enforces neither (confirmed critical finding in AUDIT_REPORT.md — it
/// trusts whatever Content-Type and size it's given with zero validation, and a
/// spoofed Content-Type has been live-exploited there for same-origin script
/// execution), so this is the only safety net an upload gets.
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
