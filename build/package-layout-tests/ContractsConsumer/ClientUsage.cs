using System.ComponentModel.DataAnnotations;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Helpers;

namespace ContractsConsumer;

/// <summary>
/// Stands in for a third-party BlogIt client, compiled against the <c>BlogIt.Contracts</c> package
/// and nothing else. Every member here exists to make the compiler resolve a type the finding says
/// a client needs, so that a contracts package which stopped carrying one fails the harness at
/// build time rather than at some consumer's runtime.
/// </summary>
/// <remarks>
/// The methods are never called. Compiling is the assertion — this is a layout fixture, not a test
/// of behaviour, and the behaviour of these types is covered by the xunit suite against source.
/// </remarks>
public static class ClientUsage
{
    /// <summary>
    /// Round-trips the request/response records a client actually sends and receives. Uses the
    /// trailing optional parameters positionally-by-name so the fixture keeps compiling if more are
    /// appended, which is the convention docs/publishing.md requires of these records.
    /// </summary>
    public static CreateBlogPostRequest BuildCreateRequest(string title, string summary) =>
        new(
            Title: title,
            Summary: summary,
            Content: null,
            SeoTitle: null,
            SeoDescription: null,
            SeoKeywords: null,
            OgImageUrl: null,
            TagNames: Array.Empty<string>());

    /// <summary>
    /// Sends an edit back with the stamp from the read it is based on. A client cannot implement
    /// optimistic concurrency without both this record and the stamp on the detail DTO, so both
    /// have to be in the contracts package rather than the engine.
    /// </summary>
    public static UpdateBlogPostRequest BuildUpdateRequest(BlogPostDetailDto post) =>
        new(
            Title: post.Title,
            Summary: post.Summary,
            Content: post.Content,
            SeoTitle: post.SeoTitle,
            SeoDescription: post.SeoDescription,
            SeoKeywords: post.SeoKeywords,
            OgImageUrl: post.OgImageUrl,
            TagNames: post.Tags.Select(tag => tag.Name).ToArray(),
            ConcurrencyStamp: post.ConcurrencyStamp);

    /// <summary>
    /// Reads the paged list shape and the tag projection a list screen binds to.
    /// </summary>
    public static IReadOnlyList<string> SummariseFirstPage(PagedResult<BlogPostSummaryDto> page) =>
        page.Items
            .Where(post => post.ScheduleState != PublicationScheduleState.Draft)
            .Select(post => $"{post.Title} ({post.Tags.Count} tags, page {page.Page}/{page.PageSize})")
            .ToArray();

    /// <summary>
    /// Builds the public path for a post without the server. This is the reason
    /// <c>BlogUrlHelper</c> lives in contracts rather than in the engine's Helpers folder: a client
    /// that renders links has to agree with the server's routing byte for byte.
    /// </summary>
    public static string GetPostPath(BlogPostSummaryDto post) =>
        BlogUrlHelper.GetPostPath(post.Slug, post.PublishedAt, post.CreatedAt);

    /// <summary>
    /// Validates before sending, which is the payoff of the DataAnnotations attributes: the client
    /// gets the server's real length limits from the same constants the server enforces, instead of
    /// discovering them from a 400. Uses the framework validator over the attributes rather than
    /// reading the constants directly, so this fails if the attributes are dropped from the DTOs.
    /// </summary>
    public static IReadOnlyList<string> ValidateBeforeSending(CreateBlogPostRequest request)
    {
        List<ValidationResult> results = [];
        Validator.TryValidateObject(
            request,
            new ValidationContext(request),
            results,
            validateAllProperties: true);
        return results.Select(result => result.ErrorMessage ?? "invalid").ToArray();
    }

    /// <summary>
    /// Reads the length ceilings directly too. A client that wants to show a character counter
    /// needs the numbers, not just the attributes.
    /// </summary>
    public static (int Title, int Slug, int SeoTitle, int RedirectSource) Limits() =>
        (ContentLimits.TitleLength,
         ContentLimits.SlugLength,
         SeoLimits.TitleLength,
         RedirectLimits.SourcePathLength);

    /// <summary>
    /// Resolves a settings key by name rather than by string literal, and reads the bootstrap
    /// document the admin shell is configured from — both of which a replacement admin client needs.
    /// </summary>
    public static string SiteNameKey() => SettingKeys.SiteName;

    /// <inheritdoc cref="SiteNameKey" />
    public static string ApiPathFrom(BlogItAdminBootstrapConfig config) => config.ApiPath;
}
