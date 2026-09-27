using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Two admins editing the same post — or one admin in the Blazor and MAUI clients, both shipped —
/// used to be a silent last-write-wins clobber with no conflict surfaced anywhere.
/// </summary>
public class ConcurrencyApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task UpdatePost_WithTheCurrentStamp_Succeeds()
    {
        var client = await AuthedClientAsync("conc_post_ok");
        var post = await CreatePostAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/posts/{post.Id}",
            UpdateFor(post, "First edit"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<BlogPostDetailDto>();
        updated!.Title.Should().Be("First edit");
        updated.ConcurrencyStamp.Should().NotBe(post.ConcurrencyStamp, "saving moves the token");
        updated.ConcurrencyStamp.Should().NotBeEmpty();
    }

    [Fact]
    public async Task UpdatePost_WithAStampFromBeforeSomeoneElseSaved_ReturnsConflict()
    {
        var client = await AuthedClientAsync("conc_post_stale");
        var post = await CreatePostAsync(client);

        // Editor A saves. Editor B is still holding the copy they loaded before that.
        var firstSave = await client.PutAsJsonAsync($"/api/posts/{post.Id}", UpdateFor(post, "Editor A"));
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondSave = await client.PutAsJsonAsync($"/api/posts/{post.Id}", UpdateFor(post, "Editor B"));

        secondSave.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await secondSave.Content.ReadAsStringAsync()).Should().Contain("changed by someone else");
    }

    [Fact]
    public async Task UpdatePost_RejectedByConflict_LeavesTheStoredPostUntouched()
    {
        var client = await AuthedClientAsync("conc_post_intact");
        var post = await CreatePostAsync(client);

        await client.PutAsJsonAsync($"/api/posts/{post.Id}", UpdateFor(post, "Editor A"));
        await client.PutAsJsonAsync($"/api/posts/{post.Id}", UpdateFor(post, "Editor B"));

        var current = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");
        current!.Title.Should().Be("Editor A", "the rejected edit must not have been applied");
    }

    [Fact]
    public async Task UpdatePost_WithNoStampAtAll_ReturnsConflict()
    {
        // Fails closed on purpose: a client that does not participate is treated as out of date
        // rather than waved through, because a skipped check is how the original defect returns.
        var client = await AuthedClientAsync("conc_post_missing");
        var post = await CreatePostAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/api/posts/{post.Id}",
            new UpdateBlogPostRequest("No stamp", "Summary", "Body", null, null, null, null, []));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdatePost_TwiceInARow_SucceedsWhenTheClientTracksTheNewStamp()
    {
        // The regression a naive implementation causes: a user saving twice without reloading.
        var client = await AuthedClientAsync("conc_post_twice");
        var post = await CreatePostAsync(client);

        var first = await client.PutAsJsonAsync($"/api/posts/{post.Id}", UpdateFor(post, "Save one"));
        var afterFirst = await first.Content.ReadFromJsonAsync<BlogPostDetailDto>();

        var second = await client.PutAsJsonAsync($"/api/posts/{post.Id}", UpdateFor(afterFirst!, "Save two"));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdatePage_WithAStaleStamp_ReturnsConflict()
    {
        var client = await AuthedClientAsync("conc_page_stale");
        var page = await CreatePageAsync(client);

        await client.PutAsJsonAsync($"/api/pages/{page.Id}", UpdateFor(page, "Editor A"));
        var secondSave = await client.PutAsJsonAsync($"/api/pages/{page.Id}", UpdateFor(page, "Editor B"));

        secondSave.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdatePage_WithTheCurrentStamp_SucceedsAndMovesIt()
    {
        var client = await AuthedClientAsync("conc_page_ok");
        var page = await CreatePageAsync(client);

        var response = await client.PutAsJsonAsync($"/api/pages/{page.Id}", UpdateFor(page, "Edited"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<PageDto>();
        updated!.Title.Should().Be("Edited");
        updated.ConcurrencyStamp.Should().NotBe(page.ConcurrencyStamp);
    }

    [Fact]
    public async Task CreatedContent_AlreadyCarriesAStamp()
    {
        var client = await AuthedClientAsync("conc_create");

        (await CreatePostAsync(client)).ConcurrencyStamp.Should().NotBeEmpty();
        (await CreatePageAsync(client)).ConcurrencyStamp.Should().NotBeEmpty();
    }

    private static UpdateBlogPostRequest UpdateFor(BlogPostDetailDto post, string title) =>
        new(title, post.Summary, post.Content, post.SeoTitle, post.SeoDescription, post.SeoKeywords,
            post.OgImageUrl, [], post.ScheduledPublishAt, post.ScheduledUnpublishAt, post.Slug,
            post.ConcurrencyStamp);

    private static UpdatePageRequest UpdateFor(PageDto page, string title) =>
        new(title, page.Slug, page.Content, page.SeoTitle, page.SeoDescription, page.SeoKeywords,
            page.OgImageUrl, page.IsPublished, page.ScheduledPublishAt, page.ScheduledUnpublishAt,
            page.ConcurrencyStamp);

    private async Task<HttpClient> AuthedClientAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        return factory.CreateClient().WithAuth(userId, username);
    }

    private static async Task<BlogPostDetailDto> CreatePostAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/posts",
            new CreateBlogPostRequest("Original", "Summary", "Body", null, null, null, null, []));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<BlogPostDetailDto>())!;
    }

    private static async Task<PageDto> CreatePageAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/pages",
            new CreatePageRequest($"Original {Guid.NewGuid():N}", "", "Body", null, null, null, null, false));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<PageDto>())!;
    }
}
