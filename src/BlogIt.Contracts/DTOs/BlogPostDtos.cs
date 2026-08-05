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
    bool HasBeenPublished = false
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
    string? Slug = null
);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
