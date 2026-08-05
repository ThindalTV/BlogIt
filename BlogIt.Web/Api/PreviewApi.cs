using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Helpers;
using BlogIt.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Web.Api;

public static class PreviewApi
{
    public static IEndpointRouteBuilder MapPreviewApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/previews")
            .WithTags("Previews")
            .RequireAuthorization();

        group.MapPost("/posts/{id:guid}", CreatePostPreview);
        group.MapPost("/pages/{id:guid}", CreatePagePreview);
        return app;
    }

    private static async Task<IResult> CreatePostPreview(
        Guid id,
        BlogItDbContext db,
        IPreviewTokenService previewTokens)
    {
        var post = await db.BlogPosts
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id);
        if (post is null)
            return Results.NotFound();

        var (token, expiresAt) = previewTokens.Issue(PreviewContentType.Post, post.Id);
        var path = BlogUrlHelper.GetPostPath(post.Slug, post.PublishedAt, post.CreatedAt);
        return Results.Ok(new PreviewLinkResponse($"{path}?preview={token:D}", expiresAt));
    }

    private static async Task<IResult> CreatePagePreview(
        Guid id,
        BlogItDbContext db,
        IPreviewTokenService previewTokens)
    {
        var page = await db.Pages
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == id);
        if (page is null)
            return Results.NotFound();

        var (token, expiresAt) = previewTokens.Issue(PreviewContentType.Page, page.Id);
        return Results.Ok(new PreviewLinkResponse($"/{page.Slug}?preview={token:D}", expiresAt));
    }
}
