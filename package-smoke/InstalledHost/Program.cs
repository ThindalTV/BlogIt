using BlogIt;
using BlogIt.Services;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("BLOGIT_SMOKE_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("BLOGIT_SMOKE_CONNECTION_STRING is required.");

var mediaRoot = Environment.GetEnvironmentVariable("BLOGIT_SMOKE_MEDIA_ROOT");
if (string.IsNullOrWhiteSpace(mediaRoot))
    throw new InvalidOperationException("BLOGIT_SMOKE_MEDIA_ROOT is required.");

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(connectionString);
    options.UseFileSystemStorage(storage => storage.RootPath = mediaRoot);
});

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseBlogIt();
app.MapBlogIt();

app.MapGet("/smoke/health", () => Results.Ok(new { status = "ready" }));
app.MapGet("/smoke/migrations", async (
    IDbContextFactory<BlogItDbContext> contextFactory,
    CancellationToken cancellationToken) =>
{
    await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
    var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
    var pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
    return Results.Ok(new { applied, pending });
});
app.MapGet("/smoke/public-surface", async (
    IPublicContentService contentService,
    CancellationToken cancellationToken) =>
{
    var posts = await contentService.GetPostsAsync(1, 1, cancellationToken);
    return Results.Ok(new
    {
        postCount = posts.Posts.Count,
        types = new[]
        {
            typeof(BlogIt.Components.Shared.SeoHead).FullName,
            typeof(IPublicContentService).FullName,
            typeof(BlogPostSummaryDto).FullName
        }
    });
});

app.Run();
