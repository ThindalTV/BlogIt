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
    long ViewCount = 0
);

public record UploadMediaRequest(string Title);
