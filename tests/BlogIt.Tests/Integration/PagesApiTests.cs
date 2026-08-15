using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

public class PagesApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task CreatePage_WithAuth_Returns201()
    {
        var userId = await factory.SeedUserAsync("page_creator");
        var client = factory.CreateClient().WithAuth(userId);

        var request = new CreatePageRequest(
            Title: "About Us",
            Slug: "about",
            Content: "# About\n\nWelcome.",
            SeoTitle: "About Us | Blog",
            SeoDescription: "Learn about us",
            SeoKeywords: null,
            OgImageUrl: null,
            IsPublished: false);

        var response = await client.PostAsJsonAsync("/api/pages", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await response.Content.ReadFromJsonAsync<PageDto>();
        page!.Title.Should().Be("About Us");
        page.Slug.Should().Be("about");
        page.IsPublished.Should().BeFalse();
    }

    [Fact]
    public async Task CreatePage_WithBlankSlug_GeneratesSlugFromTitle()
    {
        // Regression test: unlike CreatePost, CreatePage had no fallback to the title when Slug
        // was left blank, and no check that the resulting slug was non-empty — a page saved with
        // a blank Slug field became permanently unreachable (its slug locks on first publish).
        var userId = await factory.SeedUserAsync($"page_blank_slug_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var request = new CreatePageRequest(
            Title: "Contact Us",
            Slug: "",
            Content: "Content",
            SeoTitle: null, SeoDescription: null, SeoKeywords: null, OgImageUrl: null,
            IsPublished: false);

        var response = await client.PostAsJsonAsync("/api/pages", request);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var page = await response.Content.ReadFromJsonAsync<PageDto>();
        page!.Slug.Should().Be("contact-us");
    }

    [Fact]
    public async Task CreatePage_WithBlankTitle_ReturnsBadRequest()
    {
        var userId = await factory.SeedUserAsync($"page_blank_title_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var request = new CreatePageRequest(
            Title: "   ",
            Slug: "some-slug",
            Content: "Content",
            SeoTitle: null, SeoDescription: null, SeoKeywords: null, OgImageUrl: null,
            IsPublished: false);

        var response = await client.PostAsJsonAsync("/api/pages", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdatePage_WithBlankSlug_KeepsExistingSlug()
    {
        var userId = await factory.SeedUserAsync($"page_update_blank_slug_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = new CreatePageRequest(
            "Original", "original-slug", "Content", null, null, null, null, false);
        var created = await (await client.PostAsJsonAsync("/api/pages", create))
            .Content.ReadFromJsonAsync<PageDto>();

        var update = new UpdatePageRequest(
            created!.Title, "", created.Content, null, null, null, null, false,
            ConcurrencyStamp: created.ConcurrencyStamp);
        var response = await client.PutAsJsonAsync($"/api/pages/{created.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reloaded = await response.Content.ReadFromJsonAsync<PageDto>();
        reloaded!.Slug.Should().Be("original-slug");
    }

    [Fact]
    public async Task GetPages_RequiresAuth()
    {
        var response = await factory.CreateClient().GetAsync("/api/pages");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreatePage_WithPublishSchedule_ExposesScheduledState()
    {
        var userId = await factory.SeedUserAsync("scheduled_page_creator");
        var client = factory.CreateClient().WithAuth(userId);
        var publishAt = DateTime.UtcNow.AddHours(1);
        var request = new CreatePageRequest(
            "Scheduled page", "scheduled-page", "Content", null, null, null, null, false,
            publishAt, null);

        var response = await client.PostAsJsonAsync("/api/pages", request);
        var page = await response.Content.ReadFromJsonAsync<PageDto>();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        page!.ScheduledPublishAt.Should().BeCloseTo(publishAt, TimeSpan.FromMilliseconds(1));
        page.ScheduleState.Should().Be(PublicationScheduleState.ScheduledForPublishing);
    }

    [Fact]
    public async Task PageSlug_CanChangeBeforeFirstPublish_ThenRemainsLocked()
    {
        var userId = await factory.SeedUserAsync("immutable_page_slug");
        var client = factory.CreateClient().WithAuth(userId);
        var create = new CreatePageRequest(
            "Permanent path", "permanent-path", "Content", null, null, null, null, false);
        var createdResponse = await client.PostAsJsonAsync("/api/pages", create);
        var page = await createdResponse.Content.ReadFromJsonAsync<PageDto>();
        var draftUpdate = new UpdatePageRequest(
            page!.Title, "edited-draft-path", page.Content, null, null, null, null, false,
            ConcurrencyStamp: page.ConcurrencyStamp);

        // Each save moves the token, so every step carries the one the previous response returned.
        var draftResponse = await client.PutAsJsonAsync($"/api/pages/{page.Id}", draftUpdate);
        var afterDraft = await draftResponse.Content.ReadFromJsonAsync<PageDto>();
        var publish = draftUpdate with { IsPublished = true, ConcurrencyStamp = afterDraft!.ConcurrencyStamp };
        var afterPublish = await (await client.PutAsJsonAsync($"/api/pages/{page.Id}", publish))
            .Content.ReadFromJsonAsync<PageDto>();
        var unpublish = publish with { IsPublished = false, ConcurrencyStamp = afterPublish!.ConcurrencyStamp };
        var afterUnpublish = await (await client.PutAsJsonAsync($"/api/pages/{page.Id}", unpublish))
            .Content.ReadFromJsonAsync<PageDto>();
        var lockedUpdate = unpublish with
        {
            Slug = "forbidden-path",
            ConcurrencyStamp = afterUnpublish!.ConcurrencyStamp
        };
        var lockedResponse = await client.PutAsJsonAsync($"/api/pages/{page.Id}", lockedUpdate);
        var unchanged = await client.GetFromJsonAsync<PageDto>($"/api/pages/{page.Id}");

        draftResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        lockedResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unchanged!.Slug.Should().Be("edited-draft-path");
        unchanged.HasBeenPublished.Should().BeTrue();
    }

    [Fact]
    public async Task DeletePage_NonExistent_Returns404()
    {
        var userId = await factory.SeedUserAsync("page_deleter");
        var client = factory.CreateClient().WithAuth(userId);
        var response = await client.DeleteAsync($"/api/pages/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
