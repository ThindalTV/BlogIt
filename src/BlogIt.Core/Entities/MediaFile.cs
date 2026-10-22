namespace BlogIt.Entities;

public class MediaFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Opaque provider key used to read or delete the stored object.</summary>
    public string BackendUrl { get; set; } = string.Empty;

    /// <summary>Public-facing URL path served through the app proxy e.g. /media/image.jpg</summary>
    public string PublicPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional human-chosen URL path, e.g. <c>docs/cv.pdf</c>, served as <c>/media/docs/cv.pdf</c>.
    /// Null for files that are only addressed by their storage key. Always stored normalised
    /// (see <c>MediaSlug</c>), so uniqueness does not depend on the database's collation.
    /// </summary>
    public string? Slug { get; set; }

    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the file's content was last replaced; null if it never has been.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Times the file has been served through the media proxy.</summary>
    public long ViewCount { get; set; }

    public Guid UploadedByUserId { get; set; }
    public AppUser? UploadedByUser { get; set; }
}
