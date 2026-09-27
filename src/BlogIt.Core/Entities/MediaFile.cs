namespace BlogIt.Shared.Entities;

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

    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public Guid UploadedByUserId { get; set; }
    public AppUser? UploadedByUser { get; set; }
}
