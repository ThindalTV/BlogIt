namespace BlogIt.Shared.DTOs;

public record UrlRedirectDto(
    Guid Id,
    string SourcePath,
    string TargetUrl,
    bool IsPermanent,
    bool IsAutomatic,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record SaveUrlRedirectRequest(
    string SourcePath,
    string TargetUrl,
    bool IsPermanent);
