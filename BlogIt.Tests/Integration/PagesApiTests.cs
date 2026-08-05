using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

public class PagesApiTests(BlogItWebFactory factory) : IClassFixture<BlogItWebFactory>
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
            page!.Title, "edited-draft-path", page.Content, null, null, null, null, false);

        var draftResponse = await client.PutAsJsonAsync($"/api/pages/{page.Id}", draftUpdate);
        var publish = draftUpdate with { IsPublished = true };
        await client.PutAsJsonAsync($"/api/pages/{page.Id}", publish);
        var unpublish = publish with { IsPublished = false };
        await client.PutAsJsonAsync($"/api/pages/{page.Id}", unpublish);
        var lockedUpdate = unpublish with { Slug = "forbidden-path" };
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
