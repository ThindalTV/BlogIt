namespace BlogIt.Shared.DTOs;

public record MediaFileDto(
    Guid Id,
    string Title,
    string FileName,
    string ContentType,
    string PublicPath,
    long SizeBytes,
    DateTime UploadedAt,
    string UploaderDisplayName
);

public record UploadMediaRequest(string Title);
