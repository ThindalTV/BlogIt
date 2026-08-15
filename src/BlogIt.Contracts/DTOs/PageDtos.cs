namespace BlogIt.Shared.DTOs;

public record PageDto(
    Guid Id,
    string Title,
    string Slug,
    string Content,
    bool IsPublished,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    string? OgImageUrl,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    PublicationScheduleState ScheduleState = PublicationScheduleState.Draft,
    bool HasBeenPublished = false,
    /// <summary>
    /// The page's optimistic-concurrency token as of this read. Send it back in
    /// <see cref="UpdatePageRequest.ConcurrencyStamp"/>; the server rejects the update with
    /// <c>409 Conflict</c> if the page has changed since.
    /// </summary>
    Guid ConcurrencyStamp = default
);

public record CreatePageRequest(
    string Title,
    string Slug,
    string Content,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    string? OgImageUrl,
    bool IsPublished,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null
);

/// <param name="ConcurrencyStamp">
/// The <see cref="PageDto.ConcurrencyStamp"/> from the read this edit is based on. Required, and
/// fails closed — see <see cref="UpdateBlogPostRequest.ConcurrencyStamp"/> for the reasoning.
/// </param>
public record UpdatePageRequest(
    string Title,
    string Slug,
    string Content,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    string? OgImageUrl,
    bool IsPublished,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    Guid ConcurrencyStamp = default
);
