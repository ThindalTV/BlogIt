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
    bool HasBeenPublished = false
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
    DateTime? ScheduledUnpublishAt = null
);
