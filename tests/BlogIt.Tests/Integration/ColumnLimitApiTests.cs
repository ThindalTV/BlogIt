using System.Net;
using System.Net.Http.Headers;
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
/// <remarks>
/// The <c>IsRequired</c> half of the same problem lives here too: a column that refuses null fails
/// on <c>SaveChanges</c> exactly like an over-long one does, and a JSON <c>null</c> reaches it
/// straight through the non-nullable parameters of a request record without anything checking.
/// </remarks>
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

    [Fact]
    public async Task CreatePost_WithAnOverLongTitle_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_post_title");

        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            TooLong(ContentLimits.TitleLength), "Summary", "Body",
            null, null, null, null, TagNames: []));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("title");
    }

    [Fact]
    public async Task UpdatePost_WithAnOverLongTitle_ReturnsBadRequestAndKeepsTheStoredValue()
    {
        var client = await AuthedClientAsync("limits_post_title_update");
        var created = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Keep me", "Summary", "Body", null, null, null, null, []));
        var post = (await created.Content.ReadFromJsonAsync<BlogPostDetailDto>())!;

        var response = await client.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdateBlogPostRequest(
            TooLong(ContentLimits.TitleLength), "Summary", "Body", null, null, null, null, [],
            ConcurrencyStamp: post.ConcurrencyStamp));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var current = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");
        current!.Title.Should().Be("Keep me");
    }

    [Fact]
    public async Task CreatePost_WithoutASummary_ReturnsBadRequest()
    {
        // Posted as an anonymous object rather than CreateBlogPostRequest because the record's
        // Summary parameter is non-nullable and this is the shape a client that simply omits the
        // field sends. TagNames is still supplied: null there is a separate unrelated crash.
        var client = await AuthedClientAsync("limits_post_summary");

        var response = await client.PostAsJsonAsync(
            "/api/posts",
            new { title = "Title", tagNames = Array.Empty<string>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("summary");
    }

    [Fact]
    public async Task CreatePage_WithAnOverLongTitle_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_page_title");

        var response = await client.PostAsJsonAsync("/api/pages", new CreatePageRequest(
            TooLong(ContentLimits.TitleLength), "", "Body", null, null, null, null, false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("title");
    }

    [Fact]
    public async Task CreatePage_WithoutContent_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_page_content");

        var response = await client.PostAsJsonAsync(
            "/api/pages",
            new { title = $"Page {Guid.NewGuid():N}" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content");
    }

    [Theory]
    [InlineData("username")]
    [InlineData("displayName")]
    public async Task CreateUser_WithABlankRequiredField_ReturnsBadRequest(string field)
    {
        var client = await AuthedClientAsync($"limits_user_blank_{field}");

        var response = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            Username: field == "username" ? "   " : $"user_{Guid.NewGuid():N}",
            DisplayName: field == "displayName" ? "   " : "Display Name",
            Password: "Password1!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(field);
    }

    [Fact]
    public async Task CreateUser_WithAnOverLongUsername_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_user_username");

        var response = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            TooLong(ContentLimits.UsernameLength), "Display Name", "Password1!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("username");
    }

    [Fact]
    public async Task CreateUser_WithAnOverLongDisplayName_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_user_display");

        var response = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            $"user_{Guid.NewGuid():N}",
            TooLong(ContentLimits.DisplayNameLength),
            "Password1!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("displayName");
    }

    [Theory]
    [InlineData("username")]
    [InlineData("displayName")]
    public async Task Initialize_WithABlankRequiredField_ReturnsBadRequestAndCreatesNoUser(string field)
    {
        // A fresh factory because /setup/initialize refuses to run once any user exists, and the
        // shared one is full of users seeded by the tests above.
        await using var freshFactory = new BlogItSampleFactory();
        var client = freshFactory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/setup/initialize", new SetupInitializeRequest(
            Username: field == "username" ? "" : "admin",
            DisplayName: field == "displayName" ? "" : "Administrator",
            Password: "AdminPass1!",
            SiteName: "Test Blog",
            SiteUrl: "https://test.com",
            SiteDescription: "A test blog",
            DefaultOgImage: null,
            AiProvider: "openai-compatible",
            AiApiKey: "test-key",
            AiBaseUrl: null,
            AiModel: null,
            AiExportModel: null,
            GoogleAnalyticsMeasurementId: null,
            GoogleAnalyticsPropertyId: null,
            GoogleAnalyticsCredentialsJson: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(field);
        var status = await client.GetFromJsonAsync<SetupStatusResponse>("/api/setup/status");
        status!.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task CreateConversation_WithABlankTitle_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_ai_blank");

        var response = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("   "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("title");
    }

    [Fact]
    public async Task CreateConversation_WithAnOverLongTitle_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_ai_title");

        var response = await client.PostAsJsonAsync(
            "/api/ai/conversations",
            new CreateAiConversationRequest(TooLong(ContentLimits.TitleLength)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("title");
    }

    [Fact]
    public async Task UploadMedia_WithAnOverLongTitle_ReturnsBadRequestAndStoresNothing()
    {
        // Its own database, because "nothing was stored" is a statement about the whole media table
        // and the shared factory's is full of uploads from the tests around this one. It only passed
        // before because this happened to be the sole successful upload in the class.
        await using var isolated = new BlogItSampleFactory();
        var client = await AuthedClientAsync(isolated, "limits_media_title");

        var response = await UploadAsync(client, TooLong(ContentLimits.TitleLength));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("title");
        // The rejection has to happen before the file reaches the storage provider, or a 400 still
        // leaves an unreferenced blob behind.
        var listed = await client.GetFromJsonAsync<PagedResult<MediaFileDto>>("/api/media");
        listed!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task UploadMedia_WithATitleAtTheLimit_Succeeds()
    {
        var client = await AuthedClientAsync("limits_media_title_ok");

        var response = await UploadAsync(client, new string('m', ContentLimits.TitleLength));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UploadMedia_WithAnOverLongFileName_ReturnsBadRequestAndStoresNothing()
    {
        // FileName and ContentType are whatever the browser sent, so neither is bounded by anything
        // the API validated before this. Both reach columns that are.
        await using var isolated = new BlogItSampleFactory();
        var client = await AuthedClientAsync(isolated, "limits_media_filename");

        var response = await UploadAsync(
            client, "Title", fileName: TooLong(ContentLimits.FileNameLength) + ".png");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("fileName");
        var listed = await client.GetFromJsonAsync<PagedResult<MediaFileDto>>("/api/media");
        listed!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task UploadMedia_WithAnOverLongContentType_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_media_contenttype");

        // A media type has to stay syntactically valid to survive the header parser, so the length
        // is made up in the subtype rather than as one run of padding.
        var response = await UploadAsync(
            client, "Title", contentType: "image/" + new string('p', ContentLimits.ContentTypeLength));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("contentType");
    }

    [Fact]
    public async Task UploadMedia_WithAFileNameAtTheLimit_Succeeds()
    {
        var client = await AuthedClientAsync("limits_media_filename_ok");

        var response = await UploadAsync(
            client, "Title", fileName: new string('f', ContentLimits.FileNameLength));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreatePost_WithAnOverLongTagName_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_post_tag");

        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Tagged", "Summary", "Body", null, null, null, null,
            TagNames: ["fine", TooLong(ContentLimits.TagNameLength)]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("tagNames");
    }

    [Fact]
    public async Task CreatePost_WithATagNameAtTheLimit_Succeeds()
    {
        var client = await AuthedClientAsync("limits_post_tag_ok");

        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Tagged well", "Summary", "Body", null, null, null, null,
            TagNames: [new string('g', ContentLimits.TagNameLength)]));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreatePost_WithAnOverLongExplicitSlug_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_post_slug");

        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Titled", "Summary", "Body", null, null, null, null, [],
            Slug: TooLong(ContentLimits.SlugLength)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("slug");
    }

    [Fact]
    public async Task CreatePage_WithAnOverLongExplicitSlug_ReturnsBadRequest()
    {
        var client = await AuthedClientAsync("limits_page_slug");

        var response = await client.PostAsJsonAsync("/api/pages", new CreatePageRequest(
            "Titled", TooLong(ContentLimits.SlugLength), "Body", null, null, null, null, false));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("slug");
    }

    [Fact]
    public async Task UpdatePost_WithAnOverLongExplicitSlug_ReturnsBadRequestAndKeepsTheStoredSlug()
    {
        var client = await AuthedClientAsync("limits_post_slug_update");
        var created = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Keep my address", "Summary", "Body", null, null, null, null, []));
        var post = (await created.Content.ReadFromJsonAsync<BlogPostDetailDto>())!;

        var response = await client.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdateBlogPostRequest(
            post.Title, post.Summary, post.Content, null, null, null, null, [],
            Slug: TooLong(ContentLimits.SlugLength), ConcurrencyStamp: post.ConcurrencyStamp));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var current = await client.GetFromJsonAsync<BlogPostDetailDto>($"/api/posts/{post.Id}");
        current!.Slug.Should().Be(post.Slug);
    }

    private static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        string title,
        string fileName = "hero.png",
        string contentType = "image/png")
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent("stored bytes"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(title), "title");
        return await client.PostAsync("/api/media/upload", form);
    }

    private static string TooLong(int limit) => new('x', limit + 1);

    private Task<HttpClient> AuthedClientAsync(string prefix) => AuthedClientAsync(factory, prefix);

    private static async Task<HttpClient> AuthedClientAsync(BlogItSampleFactory host, string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}";
        var userId = await host.SeedUserAsync(username);
        return host.CreateClient().WithAuth(userId, username);
    }
}
