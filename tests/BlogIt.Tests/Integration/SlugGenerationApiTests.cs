using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// A slug is the only handle a published post or page ever has, and it locks on first publication —
/// so anything that produces an empty or over-long one produces content that cannot be reached and
/// cannot be repaired through the UI afterwards.
/// </summary>
/// <remarks>
/// Two ways in. A title outside the Latin alphabet slugified to nothing at all, and a title filling
/// the 500-character column produced a 502-character slug once <c>EnsureUnique</c> appended its
/// collision counter. Both are reachable through the ordinary create endpoints.
/// </remarks>
public class SlugGenerationApiTests(BlogItSampleFactory factory) : IClassFixture<BlogItSampleFactory>
{
    private const string CyrillicTitle = "Привет мир";
    private const string JapaneseTitle = "日本語のタイトル";

    [Theory]
    [InlineData(CyrillicTitle)]
    [InlineData(JapaneseTitle)]
    public async Task CreatePost_WithANonLatinTitle_ReturnsAUsableSlug(string title)
    {
        var client = await AuthedClientAsync("slug_post_nonlatin");

        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            title, "Summary", "Body", null, null, null, null, []));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var post = (await response.Content.ReadFromJsonAsync<BlogPostDetailDto>())!;
        // Anchored on an alphanumeric first character, not just on the slug alphabet: without a
        // fallback the empty slug collides with an earlier empty one and comes back as "-2", which is
        // non-empty and entirely made of slug characters while still being the bug.
        post.Slug.Should().MatchRegex("^[a-z0-9][a-z0-9-]*$");
    }

    [Fact]
    public async Task CreatePost_TwiceWithTheSameNonLatinTitle_ProducesTwoDistinctSlugs()
    {
        // The second one is the damaging case: both resolved to "" and the second violated the
        // unique index on Slug, which a real provider answers with a 500.
        //
        // Its own database, because the assertion names an exact slug and the shared factory already
        // holds a post with this title from the test above.
        await using var isolated = new BlogItSampleFactory();
        var client = await AuthedClientAsync(isolated, "slug_post_nonlatin_dup");
        var request = new CreateBlogPostRequest(
            CyrillicTitle, "Summary", "Body", null, null, null, null, []);

        var first = await client.PostAsJsonAsync("/api/posts", request);
        var second = await client.PostAsJsonAsync("/api/posts", request);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstSlug = (await first.Content.ReadFromJsonAsync<BlogPostDetailDto>())!.Slug;
        var secondSlug = (await second.Content.ReadFromJsonAsync<BlogPostDetailDto>())!.Slug;
        firstSlug.Should().NotBeEmpty();
        secondSlug.Should().Be($"{firstSlug}-2");
    }

    [Fact]
    public async Task CreatePost_WithTheSameNonLatinTitleOnTwoSites_DerivesTheSameSlug()
    {
        // Stability is the requirement that rules out a random or time-based suffix: a draft
        // previewed under one address and published under another is the same defect wearing a
        // different hat. Two independent databases stand in for two runs.
        await using var oneSite = new BlogItSampleFactory();
        await using var otherSite = new BlogItSampleFactory();
        var here = await AuthedClientAsync(oneSite, "slug_stable_a");
        var there = await AuthedClientAsync(otherSite, "slug_stable_b");
        var request = new CreateBlogPostRequest(
            JapaneseTitle, "Summary", "Body", null, null, null, null, []);

        var onOneSite = await (await here.PostAsJsonAsync("/api/posts", request))
            .Content.ReadFromJsonAsync<BlogPostDetailDto>();
        var onTheOther = await (await there.PostAsJsonAsync("/api/posts", request))
            .Content.ReadFromJsonAsync<BlogPostDetailDto>();

        onOneSite!.Slug.Should().NotBeEmpty();
        onTheOther!.Slug.Should().Be(onOneSite.Slug);
    }

    [Fact]
    public async Task CreatePost_ThreeTimesWithTheSameTitle_KeepsCountingPastTheFirstSuffix()
    {
        // The create path no longer reads every slug in the table; it asks for the ones that could
        // collide. Two posts cannot tell a correct narrowing from one that only looks for the exact
        // slug, because the second gets "-2" either way. The third is the one that proves the
        // already-suffixed rows come back too — otherwise it would be handed "-2" a second time and
        // the unique index would refuse it.
        await using var isolated = new BlogItSampleFactory();
        var client = await AuthedClientAsync(isolated, "slug_post_third");
        var request = new CreateBlogPostRequest(
            "Repeated title", "Summary", "Body", null, null, null, null, []);

        var slugs = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/posts", request);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            slugs.Add((await response.Content.ReadFromJsonAsync<BlogPostDetailDto>())!.Slug);
        }

        slugs.Should().Equal("repeated-title", "repeated-title-2", "repeated-title-3");
    }

    [Fact]
    public async Task CreatePage_ThreeTimesWithTheSameTitle_KeepsCountingPastTheFirstSuffix()
    {
        await using var isolated = new BlogItSampleFactory();
        var client = await AuthedClientAsync(isolated, "slug_page_third");
        var request = new CreatePageRequest(
            "Repeated page", "", "Content", null, null, null, null, false);

        var slugs = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var response = await client.PostAsJsonAsync("/api/pages", request);
            response.StatusCode.Should().Be(HttpStatusCode.Created);
            slugs.Add((await response.Content.ReadFromJsonAsync<PageDto>())!.Slug);
        }

        slugs.Should().Equal("repeated-page", "repeated-page-2", "repeated-page-3");
    }

    [Fact]
    public async Task CreatePage_WithANonLatinTitleAndNoSlug_IsCreatedRatherThanRejected()
    {
        // Pages already refused a blank slug rather than storing an unreachable one — correct for a
        // slug someone typed, but a dead end for a site whose titles are simply not Latin.
        var client = await AuthedClientAsync("slug_page_nonlatin");

        var response = await client.PostAsJsonAsync("/api/pages", new CreatePageRequest(
            CyrillicTitle, "", "Content", null, null, null, null, false));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var page = (await response.Content.ReadFromJsonAsync<PageDto>())!;
        page.Slug.Should().MatchRegex("^[a-z0-9][a-z0-9-]*$");
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("---")]
    public async Task CreatePost_WithAnExplicitSlugThatSlugifiesToNothing_ReturnsBadRequest(string slug)
    {
        // Deliberately different from the title case: a slug the author typed can be corrected, so
        // saying so beats substituting an opaque token they never asked for.
        var client = await AuthedClientAsync("slug_post_explicit_empty");

        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "A perfectly good title", "Summary", "Body", null, null, null, null, [], Slug: slug));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("slug");
    }

    [Fact]
    public async Task UpdatePost_WithAnExplicitSlugThatSlugifiesToNothing_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("slug_post_update_empty");
        var post = await CreatePostAsync(client, "Editable post");

        var response = await client.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdateBlogPostRequest(
            post.Title, post.Summary, post.Content, null, null, null, null, [],
            Slug: "???", ConcurrencyStamp: post.ConcurrencyStamp));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var current = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");
        current!.Slug.Should().Be(post.Slug);
    }

    [Fact]
    public async Task CreatePost_TwiceWithAMaximumLengthTitle_KeepsTheSlugInsideItsColumn()
    {
        // Finding #46. Title and Slug are both 500 wide, so the collision counter had nowhere to go.
        var client = await AuthedClientAsync("slug_post_max_title");
        var title = new string('t', ContentLimits.TitleLength);
        var request = new CreateBlogPostRequest(title, "Summary", "Body", null, null, null, null, []);

        var first = await client.PostAsJsonAsync("/api/posts", request);
        var second = await client.PostAsJsonAsync("/api/posts", request);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstSlug = (await first.Content.ReadFromJsonAsync<BlogPostDetailDto>())!.Slug;
        var secondSlug = (await second.Content.ReadFromJsonAsync<BlogPostDetailDto>())!.Slug;
        firstSlug.Length.Should().Be(ContentLimits.SlugLength);
        secondSlug.Length.Should().BeLessThanOrEqualTo(ContentLimits.SlugLength);
        secondSlug.Should().NotBe(firstSlug);
    }

    [Fact]
    public async Task CreatePage_TwiceWithAMaximumLengthTitle_KeepsTheSlugInsideItsColumn()
    {
        var client = await AuthedClientAsync("slug_page_max_title");
        var title = new string('g', ContentLimits.TitleLength);
        var request = new CreatePageRequest(title, "", "Content", null, null, null, null, false);

        var first = await client.PostAsJsonAsync("/api/pages", request);
        var second = await client.PostAsJsonAsync("/api/pages", request);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        var secondSlug = (await second.Content.ReadFromJsonAsync<PageDto>())!.Slug;
        secondSlug.Length.Should().BeLessThanOrEqualTo(ContentLimits.SlugLength);
        secondSlug.Should().NotBe((await first.Content.ReadFromJsonAsync<PageDto>())!.Slug);
    }

    private static async Task<BlogPostDetailDto> CreatePostAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            title, "Summary", "Body", null, null, null, null, []));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<BlogPostDetailDto>())!;
    }

    private Task<HttpClient> AuthedClientAsync(string prefix) => AuthedClientAsync(factory, prefix);

    private static async Task<HttpClient> AuthedClientAsync(BlogItSampleFactory host, string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}";
        var userId = await host.SeedUserAsync(username);
        return host.CreateClient().WithAuth(userId, username);
    }
}
