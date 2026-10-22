using BlogIt.Contracts.DTOs;

namespace BlogIt.Services;

/// <summary>
/// Resolves a draft for the admin's Preview button: the post or page behind a slug, returned only
/// when the current request carries a preview token (or the cookie a redeemed token leaves behind)
/// issued for exactly that item.
/// </summary>
/// <remarks>
/// Exists so a host's post and page components can support preview in one call. Before this, every
/// host had to load with <c>includeUnpublished</c>, then authorize the token against the loaded id
/// through <see cref="IPreviewTokenService"/> itself, and discard the result on failure — a host that
/// skipped that wiring (as a real one did) left the admin's Preview button opening a "not found"
/// page, and a host that got it half right would publish drafts to anyone guessing a slug.
/// </remarks>
public interface IContentPreviewService
{
    /// <summary>
    /// The post behind <paramref name="slug"/>, published or not, when <paramref name="previewToken"/>
    /// (or the preview cookie from an earlier redemption) authorizes it; otherwise <c>null</c>.
    /// </summary>
    /// <param name="previewToken">The <c>?preview=</c> query value, or <c>null</c> when absent.</param>
    Task<BlogPostDetailDto?> GetPostPreviewAsync(
        string slug,
        Guid? previewToken,
        CancellationToken cancellationToken = default);

    /// <summary>Same contract as <see cref="GetPostPreviewAsync"/>, for a custom page.</summary>
    Task<PageDto?> GetPagePreviewAsync(
        string slug,
        Guid? previewToken,
        CancellationToken cancellationToken = default);
}

public sealed class ContentPreviewService(
    IPublicContentService content,
    IPreviewTokenService previewTokens,
    IHttpContextAccessor httpContextAccessor) : IContentPreviewService
{
    public async Task<BlogPostDetailDto?> GetPostPreviewAsync(
        string slug,
        Guid? previewToken,
        CancellationToken cancellationToken = default)
    {
        // No HttpContext means an interactive circuit, where neither the query token nor the
        // preview cookie can be checked — so no preview, rather than an unauthorized one.
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return null;

        var post = (await content.GetPostAsync(
            slug, includeNavigation: false, includeUnpublished: true, cancellationToken))?.Post;
        return post is not null
            && previewTokens.TryAuthorize(httpContext, previewToken, PreviewContentType.Post, post.Id)
            ? post
            : null;
    }

    public async Task<PageDto?> GetPagePreviewAsync(
        string slug,
        Guid? previewToken,
        CancellationToken cancellationToken = default)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            return null;

        var page = await content.GetPageAsync(slug, includeUnpublished: true, cancellationToken);
        return page is not null
            && previewTokens.TryAuthorize(httpContext, previewToken, PreviewContentType.Page, page.Id)
            ? page
            : null;
    }
}
