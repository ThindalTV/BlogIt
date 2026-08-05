using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Tests.Helpers;
using BlogIt.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogIt.Tests.Integration;

public class PostsApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task GetPosts_RequiresAuth()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/posts");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetPosts_WithAuth_ReturnsPagedResult()
    {
        var userId = await factory.SeedUserAsync("posts_reader");
        await factory.SeedPostAsync(userId);
        var client = factory.CreateClient().WithAuth(userId);

        var result = await client.GetFromJsonAsync<PagedResult<BlogPostSummaryDto>>("/api/posts");
        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreatePost_WithAuth_Returns201WithSlug()
    {
        var userId = await factory.SeedUserAsync("post_creator");
        var client = factory.CreateClient().WithAuth(userId);

        var request = new CreateBlogPostRequest(
            Title: "My New Post",
            Summary: "A summary",
            Content: "# Hello\n\nContent here.",
            SeoTitle: null, SeoDescription: null, SeoKeywords: null, OgImageUrl: null,
            TagNames: ["dotnet", "blazor"]);

        var response = await client.PostAsJsonAsync("/api/posts", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var post = await response.Content.ReadFromJsonAsync<BlogPostDetailDto>();
        post.Should().NotBeNull();
        post!.Title.Should().Be("My New Post");
        post.Slug.Should().Be("my-new-post");
        post.Tags.Should().HaveCount(2);
        post.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task CreatePost_GeneratesUniqueSlug_WhenDuplicate()
    {
        var userId = await factory.SeedUserAsync("slug_test_user");
        var client = factory.CreateClient().WithAuth(userId);

        var req = new CreateBlogPostRequest("Duplicate Title", "s", null, null, null, null, null, []);
        await client.PostAsJsonAsync("/api/posts", req);
        var second = await client.PostAsJsonAsync("/api/posts", req);
        var post = await second.Content.ReadFromJsonAsync<BlogPostDetailDto>();

        post!.Slug.Should().Be("duplicate-title-2");
    }

    [Fact]
    public async Task CreatePost_WithSchedule_ExposesScheduledState()
    {
        var userId = await factory.SeedUserAsync("scheduled_post_creator");
        var client = factory.CreateClient().WithAuth(userId);
        var publishAt = DateTime.UtcNow.AddHours(2);
        var unpublishAt = publishAt.AddDays(1);
        var request = new CreateBlogPostRequest(
            "Scheduled post", "Summary", null, null, null, null, null, [],
            publishAt, unpublishAt);

        var response = await client.PostAsJsonAsync("/api/posts", request);
        var post = await response.Content.ReadFromJsonAsync<BlogPostDetailDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        post!.ScheduledPublishAt.Should().BeCloseTo(publishAt, TimeSpan.FromMilliseconds(1));
        post.ScheduledUnpublishAt.Should().BeCloseTo(unpublishAt, TimeSpan.FromMilliseconds(1));
        post.ScheduleState.Should().Be(PublicationScheduleState.ScheduledForPublishing);
    }

    [Fact]
    public async Task CreatePost_WhenUnpublishPrecedesPublish_ReturnsValidationError()
    {
        var userId = await factory.SeedUserAsync("invalid_schedule_creator");
        var client = factory.CreateClient().WithAuth(userId);
        var publishAt = DateTime.UtcNow.AddDays(2);
        var request = new CreateBlogPostRequest(
            "Invalid schedule", "Summary", null, null, null, null, null, [],
            publishAt, publishAt.AddMinutes(-1));

        var response = await client.PostAsJsonAsync("/api/posts", request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Scheduler_ProcessesDuePostPublishAndPageUnpublish()
    {
        var userId = await factory.SeedUserAsync("schedule_processor");
        var scheduledAt = DateTime.UtcNow.AddMinutes(-1);
        Guid postId;
        Guid unpublishedPostId;
        Guid republishedPostId;
        Guid pageId;
        Guid publishedPageId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
            var post = new BlogPost
            {
                Title = "Due post",
                Slug = $"due-post-{Guid.NewGuid():N}",
                Summary = "Summary",
                AuthorId = userId,
                ScheduledPublishAt = scheduledAt
            };
            var page = new Page
            {
                Title = "Due page",
                Slug = $"due-page-{Guid.NewGuid():N}",
                Content = "Content",
                IsPublished = true,
                HasBeenPublished = true,
                ScheduledUnpublishAt = scheduledAt
            };
            var unpublishedPost = new BlogPost
            {
                Title = "Post to unpublish",
                Slug = $"unpublish-post-{Guid.NewGuid():N}",
                Summary = "Summary",
                AuthorId = userId,
                IsPublished = true,
                PublishedAt = scheduledAt.AddDays(-1),
                ScheduledUnpublishAt = scheduledAt
            };
            var publishedPage = new Page
            {
                Title = "Page to publish",
                Slug = $"publish-page-{Guid.NewGuid():N}",
                Content = "Content",
                ScheduledPublishAt = scheduledAt
            };
            var republishedPost = new BlogPost
            {
                Title = "Post to republish",
                Slug = $"republish-post-{Guid.NewGuid():N}",
                Summary = "Summary",
                AuthorId = userId,
                HasBeenPublished = true,
                PublishedAt = scheduledAt.AddYears(-1),
                ScheduledPublishAt = scheduledAt
            };
            db.AddRange(post, page, unpublishedPost, publishedPage, republishedPost);
            await db.SaveChangesAsync();
            postId = post.Id;
            unpublishedPostId = unpublishedPost.Id;
            republishedPostId = republishedPost.Id;
            pageId = page.Id;
            publishedPageId = publishedPage.Id;
        }

        var scheduler = new PublicationSchedulingService(
            factory.Services.GetRequiredService<IDbContextFactory<BlogItDbContext>>(),
            factory.Services.GetRequiredService<TimeProvider>(),
            factory.Services.GetRequiredService<ILogger<PublicationSchedulingService>>());
        await scheduler.ProcessDueSchedulesAsync();

        using var verificationScope = factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        var updatedPost = await verificationDb.BlogPosts.FindAsync(postId);
        var verifiedUnpublishedPost = await verificationDb.BlogPosts.FindAsync(unpublishedPostId);
        var verifiedRepublishedPost = await verificationDb.BlogPosts.FindAsync(republishedPostId);
        var updatedPage = await verificationDb.Pages.FindAsync(pageId);
        var verifiedPublishedPage = await verificationDb.Pages.FindAsync(publishedPageId);
        updatedPost!.IsPublished.Should().BeTrue();
        updatedPost.HasBeenPublished.Should().BeTrue();
        updatedPost.PublishedAt.Should().Be(scheduledAt);
        updatedPost.ScheduledPublishAt.Should().BeNull();
        verifiedUnpublishedPost!.IsPublished.Should().BeFalse();
        verifiedUnpublishedPost.PublishedAt.Should().Be(scheduledAt.AddDays(-1));
        verifiedUnpublishedPost.ScheduledUnpublishAt.Should().BeNull();
        verifiedRepublishedPost!.IsPublished.Should().BeTrue();
        verifiedRepublishedPost.PublishedAt.Should().Be(scheduledAt.AddYears(-1));
        updatedPage!.IsPublished.Should().BeFalse();
        updatedPage.ScheduledUnpublishAt.Should().BeNull();
        verifiedPublishedPage!.IsPublished.Should().BeTrue();
        verifiedPublishedPage.HasBeenPublished.Should().BeTrue();
        verifiedPublishedPage.ScheduledPublishAt.Should().BeNull();
    }

    [Fact]
    public async Task PublishPost_SetsIsPublishedAndPublishedAt()
    {
        var userId = await factory.SeedUserAsync("publisher");
        var post = await factory.SeedPostAsync(userId, published: false);
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.PostAsync($"/api/posts/{post.Id}/publish", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");
        updated!.IsPublished.Should().BeTrue();
        updated.HasBeenPublished.Should().BeTrue();
        updated.PublishedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UnpublishPost_PreservesCanonicalPublishedAt()
    {
        var userId = await factory.SeedUserAsync("unpublisher");
        var post = await factory.SeedPostAsync(userId, published: true);
        var client = factory.CreateClient().WithAuth(userId);

        await client.PostAsync($"/api/posts/{post.Id}/unpublish", null);
        var updated = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");
        updated!.IsPublished.Should().BeFalse();
        updated.PublishedAt.Should().Be(post.PublishedAt);
    }

    [Fact]
    public async Task RepublishPost_PreservesOriginalPublishedAt()
    {
        var userId = await factory.SeedUserAsync("republisher");
        var post = await factory.SeedPostAsync(userId, published: true);
        var originalPublishedAt = post.PublishedAt;
        var client = factory.CreateClient().WithAuth(userId);

        await client.PostAsync($"/api/posts/{post.Id}/unpublish", null);
        await client.PostAsync($"/api/posts/{post.Id}/publish", null);
        var republished = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");

        republished!.IsPublished.Should().BeTrue();
        republished.PublishedAt.Should().Be(originalPublishedAt);
    }

    [Fact]
    public async Task PostSlug_CanChangeBeforeFirstPublish_ThenRemainsLocked()
    {
        var userId = await factory.SeedUserAsync("post_slug_lock");
        var client = factory.CreateClient().WithAuth(userId);
        var create = new CreateBlogPostRequest(
            "Slug lock post", "Summary", null, null, null, null, null, [],
            Slug: "initial-post-path");
        var createdResponse = await client.PostAsJsonAsync("/api/posts", create);
        var post = await createdResponse.Content.ReadFromJsonAsync<BlogPostDetailDto>();
        var draftUpdate = new UpdateBlogPostRequest(
            post!.Title, post.Summary, post.Content, null, null, null, null, [],
            Slug: "edited-draft-path");

        var draftResponse = await client.PutAsJsonAsync($"/api/posts/{post.Id}", draftUpdate);
        await client.PostAsync($"/api/posts/{post.Id}/publish", null);
        await client.PostAsync($"/api/posts/{post.Id}/unpublish", null);
        var lockedUpdate = draftUpdate with { Slug = "forbidden-path" };
        var lockedResponse = await client.PutAsJsonAsync($"/api/posts/{post.Id}", lockedUpdate);
        var unchanged = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");

        draftResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        lockedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unchanged!.Slug.Should().Be("edited-draft-path");
        unchanged.HasBeenPublished.Should().BeTrue();
    }

    [Fact]
    public async Task DeletePost_Returns204()
    {
        var userId = await factory.SeedUserAsync("deleter");
        var post = await factory.SeedPostAsync(userId);
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.DeleteAsync($"/api/posts/{post.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task GetPost_NonExistent_Returns404()
    {
        var userId = await factory.SeedUserAsync("getter_404");
        var client = factory.CreateClient().WithAuth(userId);
        var response = await client.GetAsync($"/api/posts/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
