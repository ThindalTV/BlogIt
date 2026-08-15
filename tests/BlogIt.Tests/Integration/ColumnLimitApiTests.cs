using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// The boundary half of the column-width work. Giving a column a width fixes the storage cost but
/// turns an over-long value into a database error surfacing as a 500 — so the same limits are
/// enforced at the API, returning a 400 that names the field. A test per field, because the two
/// numbers have to stay in step.
/// </summary>
public class ColumnLimitApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    [Theory]
    [InlineData("seoTitle")]
    [InlineData("seoDescription")]
    [InlineData("seoKeywords")]
    [InlineData("ogImageUrl")]
    public async Task CreatePost_WithAnOverLongSeoField_ReturnsBadRequest(string field)
    {
        var client = await AuthedClientAsync("limits_post_create");

        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Title", "Summary", "Body",
            SeoTitle: field == "seoTitle" ? TooLong(SeoLimits.TitleLength) : null,
            SeoDescription: field == "seoDescription" ? TooLong(SeoLimits.DescriptionLength) : null,
            SeoKeywords: field == "seoKeywords" ? TooLong(SeoLimits.KeywordsLength) : null,
            OgImageUrl: field == "ogImageUrl" ? TooLong(SeoLimits.OgImageUrlLength) : null,
            TagNames: []));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(field);
    }

    [Fact]
    public async Task CreatePost_AtExactlyTheLimit_Succeeds()
    {
        var client = await AuthedClientAsync("limits_post_exact");

        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Title", "Summary", "Body",
            new string('t', SeoLimits.TitleLength),
            new string('d', SeoLimits.DescriptionLength),
            new string('k', SeoLimits.KeywordsLength),
            new string('u', SeoLimits.OgImageUrlLength),
            TagNames: []));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task UpdatePost_WithAnOverLongSeoField_ReturnsBadRequestAndKeepsTheStoredValue()
    {
        var client = await AuthedClientAsync("limits_post_update");
        var created = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Title", "Summary", "Body", "Keep me", null, null, null, []));
        var post = (await created.Content.ReadFromJsonAsync<BlogPostDetailDto>())!;

        var response = await client.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdateBlogPostRequest(
            "Title", "Summary", "Body", TooLong(SeoLimits.TitleLength), null, null, null, [],
            ConcurrencyStamp: post.ConcurrencyStamp));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var current = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");
        current!.SeoTitle.Should().Be("Keep me");
    }

    [Fact]
    public async Task CreatePage_WithAnOverLongSeoField_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_page_create");

        var response = await client.PostAsJsonAsync("/api/pages", new CreatePageRequest(
            $"Title {Guid.NewGuid():N}", "", "Body",
            TooLong(SeoLimits.TitleLength), null, null, null, false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateRedirect_WithASourcePathOverTheIndexLimit_ReturnsBadRequest()
    {
        // 450 characters is set by SQL Server's 1700-byte index key limit, not by anything about
        // URLs. Above it the unique index on the column fails at insert time.
        var client = await AuthedClientAsync("limits_redirect");

        var response = await client.PostAsJsonAsync("/api/redirects", new SaveUrlRedirectRequest(
            "/" + new string('a', RedirectLimits.SourcePathLength), "/target", true));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateRedirect_AtTheSourcePathLimit_Succeeds()
    {
        var client = await AuthedClientAsync("limits_redirect_ok");

        var response = await client.PostAsJsonAsync("/api/redirects", new SaveUrlRedirectRequest(
            "/" + new string('b', RedirectLimits.SourcePathLength - 1), "/target", true));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static string TooLong(int limit) => new('x', limit + 1);

    private async Task<HttpClient> AuthedClientAsync(string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        return factory.CreateClient().WithAuth(userId, username);
    }
}
