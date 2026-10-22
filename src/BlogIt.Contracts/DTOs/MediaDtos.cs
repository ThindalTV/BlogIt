namespace BlogIt.Contracts.DTOs;

public record MediaFileDto(
    Guid Id,
    string Title,
    string FileName,
    string ContentType,
    string PublicPath,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string UploaderDisplayName,
    long ViewCount = 0,
    string? Slug = null
);

public record UploadMediaRequest(string Title);

/// <summary>
/// Edits a media file's metadata. <paramref name="Slug"/> is the human-chosen URL path
/// (<c>docs/cv.pdf</c>); null or blank removes it, leaving the file at its storage-key URL.
/// </summary>
public record UpdateMediaRequest(string Title, string? Slug);
