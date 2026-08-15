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

public record CreateBlogPostRequest(
    string Title,
    string Summary,
    string? Content,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    string? OgImageUrl,
    IReadOnlyList<string> TagNames,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    string? Slug = null
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
public record UpdateBlogPostRequest(
    string Title,
    string Summary,
    string? Content,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    string? OgImageUrl,
    IReadOnlyList<string> TagNames,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    string? Slug = null,
    Guid ConcurrencyStamp = default
);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
