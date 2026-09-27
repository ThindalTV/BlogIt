using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Pages could be scheduled but never filtered by state, unlike posts: GET /api/pages accepted
/// only a search term, so an admin with a long page list had no way to ask "what is queued?".
/// The filter values mirror the posts endpoint exactly, since both feed the same admin screens.
/// </summary>
public class PagesStatusFilterTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    private async Task<HttpClient> AuthedClientAsync(string prefix)
    {
        var userId = await factory.SeedUserAsync($"{prefix}_{Guid.NewGuid():N}");
        return factory.CreateClient().WithAuth(userId);
    }

    private static CreatePageRequest NewPage(string title, string slug, bool published, DateTime? publishAt = null) =>
        new(Title: title,
            Slug: slug,
            Content: "Content",
            SeoTitle: null, SeoDescription: null, SeoKeywords: null, OgImageUrl: null,
            IsPublished: published,
            ScheduledPublishAt: publishAt);

    private static async Task<PagedResult<PageDto>> ListAsync(HttpClient client, string status, string q)
    {
        var response = await client.GetAsync($"/api/pages?q={q}&status={status}&pageSize=50");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PagedResult<PageDto>>())!;
    }

    [Fact]
    public async Task StatusFilterSeparatesPublishedDraftAndScheduledPages()
    {
        var client = await AuthedClientAsync("page_status");
        var marker = $"statusfilter{Guid.NewGuid():N}"[..24];

        await client.PostAsJsonAsync("/api/pages", NewPage($"{marker} live", $"{marker}-live", published: true));
        await client.PostAsJsonAsync("/api/pages", NewPage($"{marker} draft", $"{marker}-draft", published: false));
        await client.PostAsJsonAsync("/api/pages", NewPage(
            $"{marker} queued", $"{marker}-queued", published: false,
            publishAt: DateTime.UtcNow.AddDays(3)));

        var published = await ListAsync(client, "published", marker);
        published.Items.Should().OnlyContain(p => p.IsPublished);
        published.Items.Should().ContainSingle(p => p.Slug == $"{marker}-live");

        var drafts = await ListAsync(client, "draft", marker);
        drafts.Items.Should().OnlyContain(p => !p.IsPublished);
        drafts.Items.Should().Contain(p => p.Slug == $"{marker}-draft");

        var scheduled = await ListAsync(client, "scheduled", marker);
        scheduled.Items.Should().ContainSingle(p => p.Slug == $"{marker}-queued",
            "only the page with a schedule attached is scheduled");
        scheduled.Items.Should().NotContain(p => p.Slug == $"{marker}-draft");
    }

    [Fact]
    public async Task UnknownOrMissingStatusReturnsEverything()
    {
        var client = await AuthedClientAsync("page_status_all");
        var marker = $"statusall{Guid.NewGuid():N}"[..22];

        await client.PostAsJsonAsync("/api/pages", NewPage($"{marker} live", $"{marker}-live", published: true));
        await client.PostAsJsonAsync("/api/pages", NewPage($"{marker} draft", $"{marker}-draft", published: false));

        var all = await ListAsync(client, "all", marker);
        all.Items.Should().HaveCount(2);

        var unfiltered = await client.GetFromJsonAsync<PagedResult<PageDto>>($"/api/pages?q={marker}&pageSize=50");
        unfiltered!.Items.Should().HaveCount(2, "an absent status must not filter anything out");
    }
}
