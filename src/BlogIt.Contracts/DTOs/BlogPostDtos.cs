using System.ComponentModel.DataAnnotations;

namespace BlogIt.Shared.DTOs;

/// <remarks>
/// Every timestamp BlogIt returns is a <see cref="DateTimeOffset"/> at offset zero — a UTC instant
/// that says so. Format with <c>"o"</c> for anything that needs an offset (Open Graph's
/// <c>article:published_time</c>, Atom, JSON), or call <see cref="DateTimeOffset.ToLocalTime"/> to
/// display it. These were <see cref="DateTime"/> before, which came back
/// <see cref="DateTimeKind.Unspecified"/> and formatted with no offset at all.
/// </remarks>
public record BlogPostSummaryDto(
    Guid Id,
    string Title,
    string Slug,
    string Summary,
    bool HasFullContent,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string AuthorDisplayName,
    IReadOnlyList<TagDto> Tags,
    DateTimeOffset? ScheduledPublishAt = null,
    DateTimeOffset? ScheduledUnpublishAt = null,
    PublicationScheduleState ScheduleState = PublicationScheduleState.Draft,
    bool HasBeenPublished = false
)
{
    /// <summary>
    /// Words in the post's body, or null when it has never been computed for this row.
    /// </summary>
    /// <remarks>
    /// Counted from <c>Content</c> only, never the summary — a summary-only post has no article to
    /// read and reports <c>0</c>. Carried on the summary DTO specifically so a listing can show a
    /// reading time without loading every body: the listing queries deliberately do not select
    /// <c>Content</c>, which is why <see cref="HasFullContent"/> exists, so this is the one figure a
    /// host cannot derive for itself. How many words per minute to assume, and whether to show the
    /// figure at all, stays the host's decision.
    /// </remarks>
    public int? WordCount { get; init; }
}

public record BlogPostDetailDto(
    Guid Id,
    string Title,
    string Slug,
    string Summary,
    string? Content,
    bool HasFullContent,
    bool IsPublished,
    DateTimeOffset? PublishedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid AuthorId,
    string AuthorDisplayName,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    string? OgImageUrl,
    IReadOnlyList<TagDto> Tags,
    DateTimeOffset? ScheduledPublishAt = null,
    DateTimeOffset? ScheduledUnpublishAt = null,
    PublicationScheduleState ScheduleState = PublicationScheduleState.Draft,
    bool HasBeenPublished = false,
    /// <summary>
    /// The post's optimistic-concurrency token as of this read. Send it back in
    /// <see cref="UpdateBlogPostRequest.ConcurrencyStamp"/> to prove the edit is based on the
    /// current version; the server rejects the update with <c>409 Conflict</c> if it has moved on.
    /// </summary>
    Guid ConcurrencyStamp = default
)
{
    /// <inheritdoc cref="BlogPostSummaryDto.WordCount"/>
    public int? WordCount { get; init; }
}

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
    /// <remarks>
    /// Optional: a post with no tags omits it, or sends null or an empty list. Omitting it used
    /// to bind as null and throw out of TagResolver as an unhandled 500 rather than a 400.
    /// </remarks>
    IReadOnlyList<string>? TagNames = null,
    DateTimeOffset? ScheduledPublishAt = null,
    DateTimeOffset? ScheduledUnpublishAt = null,
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
    /// <remarks>
    /// Optional: a post with no tags omits it, or sends null or an empty list. Omitting it used
    /// to bind as null and throw out of TagResolver as an unhandled 500 rather than a 400.
    /// </remarks>
    IReadOnlyList<string>? TagNames = null,
    DateTimeOffset? ScheduledPublishAt = null,
    DateTimeOffset? ScheduledUnpublishAt = null,
    [property: StringLength(ContentLimits.SlugLength)] string? Slug = null,
    Guid ConcurrencyStamp = default
);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
