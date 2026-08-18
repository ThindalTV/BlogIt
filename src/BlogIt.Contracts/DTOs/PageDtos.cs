using System.ComponentModel.DataAnnotations;

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

/// <remarks>See <see cref="CreateBlogPostRequest"/> for why these attributes are a subset.</remarks>
public record CreatePageRequest(
    [property: Required][property: StringLength(ContentLimits.TitleLength)] string Title,
    [property: Required][property: StringLength(ContentLimits.SlugLength)] string Slug,
    [property: Required] string Content,
    [property: StringLength(SeoLimits.TitleLength)] string? SeoTitle,
    [property: StringLength(SeoLimits.DescriptionLength)] string? SeoDescription,
    [property: StringLength(SeoLimits.KeywordsLength)] string? SeoKeywords,
    [property: StringLength(SeoLimits.OgImageUrlLength)] string? OgImageUrl,
    bool IsPublished,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null
);

/// <param name="ConcurrencyStamp">
/// The <see cref="PageDto.ConcurrencyStamp"/> from the read this edit is based on. Required, and
/// fails closed — see <see cref="UpdateBlogPostRequest.ConcurrencyStamp"/> for the reasoning.
/// </param>
public record UpdatePageRequest(
    [property: Required][property: StringLength(ContentLimits.TitleLength)] string Title,
    [property: Required][property: StringLength(ContentLimits.SlugLength)] string Slug,
    [property: Required] string Content,
    [property: StringLength(SeoLimits.TitleLength)] string? SeoTitle,
    [property: StringLength(SeoLimits.DescriptionLength)] string? SeoDescription,
    [property: StringLength(SeoLimits.KeywordsLength)] string? SeoKeywords,
    [property: StringLength(SeoLimits.OgImageUrlLength)] string? OgImageUrl,
    bool IsPublished,
    DateTime? ScheduledPublishAt = null,
    DateTime? ScheduledUnpublishAt = null,
    Guid ConcurrencyStamp = default
);
