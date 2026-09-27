using System.Net;
using System.Net.Http.Json;
using System.Text;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// A post with no tags may leave the tag list out entirely. It could not before: the JSON binder
/// supplied <c>null</c> for the absent property, <c>CreateBlogPostRequest.TagNames</c> was declared
/// non-nullable so nothing validated it, and <c>TagResolver</c> dereferenced it — surfacing an
/// <c>ArgumentNullException</c> as a <c>500</c> with a stack trace instead of a <c>400</c>. The
/// bundled Blazor admin always sent the property, so only API clients ever hit it, which matters
/// more now that <c>BlogIt.Contracts</c> ships as its own package.
/// </summary>
public class PostTagListOptionalTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    private static StringContent Json(string body) =>
        new(body, Encoding.UTF8, "application/json");

    [Fact]
    public async Task CreatePost_WithTheTagListOmittedEntirely_Succeeds()
    {
        var userId = await factory.SeedUserAsync($"tags_omitted_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        // Deliberately hand-written JSON: constructing the DTO would supply the default and never
        // reproduce an absent property.
        var response = await client.PostAsync("/api/posts", Json(
            """
            {
              "title": "No tag list at all",
              "summary": "A summary",
              "content": "Body"
            }
            """));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<BlogPostDetailDto>();
        created!.Tags.Should().BeEmpty();
    }

    [Fact]
    public async Task CreatePost_WithAnExplicitlyNullTagList_Succeeds()
    {
        var userId = await factory.SeedUserAsync($"tags_null_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.PostAsync("/api/posts", Json(
            """
            {
              "title": "Null tag list",
              "summary": "A summary",
              "content": "Body",
              "tagNames": null
            }
            """));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UpdatePost_WithTheTagListOmitted_Succeeds()
    {
        var userId = await factory.SeedUserAsync($"tags_omitted_update_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var create = new CreateBlogPostRequest(
            Title: "Starts with tags",
            Summary: "A summary",
            Content: "Body",
            SeoTitle: null, SeoDescription: null, SeoKeywords: null, OgImageUrl: null,
            TagNames: ["dotnet"]);
        var created = await (await client.PostAsJsonAsync("/api/posts", create))
            .Content.ReadFromJsonAsync<BlogPostDetailDto>();

        var response = await client.PutAsync($"/api/posts/{created!.Id}", Json(
            $$"""
            {
              "title": "Tags removed",
              "summary": "A summary",
              "content": "Body",
              "concurrencyStamp": "{{created.ConcurrencyStamp}}"
            }
            """));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BlankOptionalTextIsStoredAsNull_ButAnExplicitEmptyStringIsKept()
    {
        // null means "does not exist"; "" means "exists and is empty". The API stores what it is
        // given either way — normalising a blank field to null is the client's job.
        var userId = await factory.SeedUserAsync($"seo_null_vs_empty_{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var omitted = await (await client.PostAsync("/api/posts", Json(
            """{ "title": "Omitted SEO", "summary": "s", "content": "c" }""")))
            .Content.ReadFromJsonAsync<BlogPostDetailDto>();

        var empty = await (await client.PostAsync("/api/posts", Json(
            """{ "title": "Empty SEO", "summary": "s", "content": "c", "seoTitle": "" }""")))
            .Content.ReadFromJsonAsync<BlogPostDetailDto>();

        omitted!.SeoTitle.Should().BeNull();
        empty!.SeoTitle.Should().Be("");
    }
}
