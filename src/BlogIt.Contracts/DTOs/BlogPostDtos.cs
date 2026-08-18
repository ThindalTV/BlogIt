using System.ComponentModel.DataAnnotations;

namespace BlogIt.Shared.DTOs;

public record BlogPostSummaryDto(
    Guid Id,
    string Title,
    string Slug,
    string Summary,
    bool HasFullContent,
    bool IsPublished,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string AuthorDisplayName,
    IReadOnlyList<TagDto> Tags,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    PublicationScheduleState ScheduleState = PublicationScheduleState.Draft,
    bool HasBeenPublished = false
);

public record BlogPostDetailDto(
    Guid Id,
    string Title,
    string Slug,
    string Summary,
    string? Content,
    bool HasFullContent,
    bool IsPublished,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid AuthorId,
    string AuthorDisplayName,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    string? OgImageUrl,
    IReadOnlyList<TagDto> Tags,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    PublicationScheduleState ScheduleState = PublicationScheduleState.Draft,
    bool HasBeenPublished = false,
    /// <summary>
    /// The post's optimistic-concurrency token as of this read. Send it back in
    /// <see cref="UpdateBlogPostRequest.ConcurrencyStamp"/> to prove the edit is based on the
    /// current version; the server rejects the update with <c>409 Conflict</c> if it has moved on.
    /// </summary>
    Guid ConcurrencyStamp = default
);

/// <remarks>
/// The DataAnnotations attributes below are the client-facing half of rules the server enforces
/// anyway. Every ceiling references a constant from this assembly rather than repeating a number, so
/// there is one source of truth shared with the EF column width and the server-side check.
/// <para>
/// Deliberately incomplete: <c>Summary</c> and <c>Content</c> are <c>nvarchar(max)</c> so nothing
/// bounds them, and the slug's character rules, the URL scheme check and the tag-name rules stay
/// with <c>SlugHelper</c>, <c>UrlValidator</c> and <c>TextFieldValidator</c> in the engine —
/// contracts cannot reference those without a circular dependency, and copying their numbers here
/// would create a second source of truth that drifts. Passing these attributes is not a promise the
/// server will accept the payload; a <c>400</c> is still the last word.
/// </para>
/// </remarks>
public record CreateBlogPostRequest(
    [property: Required][property: StringLength(ContentLimits.TitleLength)] string Title,
    [property: Required] string Summary,
    string? Content,
    [property: StringLength(SeoLimits.TitleLength)] string? SeoTitle,
    [property: StringLength(SeoLimits.DescriptionLength)] string? SeoDescription,
    [property: StringLength(SeoLimits.KeywordsLength)] string? SeoKeywords,
    [property: StringLength(SeoLimits.OgImageUrlLength)] string? OgImageUrl,
    IReadOnlyList<string> TagNames,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    [property: StringLength(ContentLimits.SlugLength)] string? Slug = null
);

/// <param name="ConcurrencyStamp">
/// The <see cref="BlogPostDetailDto.ConcurrencyStamp"/> from the read this edit is based on.
/// <para>
/// Required, and deliberately fails closed: an omitted or stale value is rejected with
/// <c>409 Conflict</c> rather than silently overwriting whatever the post now contains. Without it
/// two people editing the same post — or the same person in the Blazor and MAUI clients — was a
/// last-write-wins clobber that surfaced no conflict at all. Read the post, edit, send the stamp
/// back; on a 409, re-read and let the user decide.
/// </para>
/// </param>
/// <remarks>See <see cref="CreateBlogPostRequest"/> for why these attributes are a subset.</remarks>
public record UpdateBlogPostRequest(
    [property: Required][property: StringLength(ContentLimits.TitleLength)] string Title,
    [property: Required] string Summary,
    string? Content,
    [property: StringLength(SeoLimits.TitleLength)] string? SeoTitle,
    [property: StringLength(SeoLimits.DescriptionLength)] string? SeoDescription,
    [property: StringLength(SeoLimits.KeywordsLength)] string? SeoKeywords,
    [property: StringLength(SeoLimits.OgImageUrlLength)] string? OgImageUrl,
    IReadOnlyList<string> TagNames,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    [property: StringLength(ContentLimits.SlugLength)] string? Slug = null,
    Guid ConcurrencyStamp = default
);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
