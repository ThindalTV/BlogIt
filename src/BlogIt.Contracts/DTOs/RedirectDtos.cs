using System.ComponentModel.DataAnnotations;

namespace BlogIt.Shared.DTOs;

public record UrlRedirectDto(
    Guid Id,
    string SourcePath,
    string TargetUrl,
    bool IsPermanent,
    bool IsAutomatic,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <remarks>
/// The source-path ceiling is not cosmetic: <see cref="RedirectLimits.SourcePathLength"/> is what
/// keeps the unique index on that column inside SQL Server's key-size limit, so a client that knows
/// it can stop a request that would otherwise fail at insert time. The scheme and shape rules for
/// <c>TargetUrl</c> stay with the engine's <c>UrlValidator</c> — see
/// <see cref="CreateBlogPostRequest"/> for why those are not restated here.
/// </remarks>
public record SaveUrlRedirectRequest(
    [property: Required][property: StringLength(RedirectLimits.SourcePathLength)] string SourcePath,
    [property: Required][property: StringLength(RedirectLimits.TargetUrlLength)] string TargetUrl,
    bool IsPermanent);
