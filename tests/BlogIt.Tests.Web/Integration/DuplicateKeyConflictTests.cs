using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Finding #24. Slugs, tags and usernames are each read to check for a conflict and then inserted.
/// The unique indexes keep the data correct either way — that is the part that matters — but the
/// request that loses the race got an unhandled <c>DbUpdateException</c>, a bare 500, instead of the
/// 409 the surrounding code plainly intends.
/// </summary>
/// <remarks>
/// The failure is injected rather than raced; <see cref="SaveFailureFactory"/> explains why that is
/// the only option against the InMemory provider, and both exception shapes are covered because a
/// real provider and the test provider disagree about which one a duplicate key produces.
/// </remarks>
public class DuplicateKeyConflictTests
{
    [Fact]
    public async Task CreatePost_WhenTheInsertLosesASlugRace_ReturnsConflict()
    {
        await using var factory = new SaveFailureFactory();
        var client = await AuthedClientAsync(factory, "dup_post");

        factory.Failures.NextFailure = SaveFailureSwitch.DuplicateKeyOnRelationalProvider();
        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Contested title", "Summary", "Body", null, null, null, null, []));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("try again");
    }

    [Fact]
    public async Task CreatePost_WhenTheInsertLosesATagRace_ReturnsConflictOnTheInMemoryShapeToo()
    {
        // EF Core's InMemory provider throws a bare ArgumentException where a relational provider
        // throws DbUpdateException. Handling only one of the two is untestable by construction.
        await using var factory = new SaveFailureFactory();
        var client = await AuthedClientAsync(factory, "dup_post_tags");

        factory.Failures.NextFailure = SaveFailureSwitch.DuplicateKeyOnInMemoryProvider();
        var response = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Tagged title", "Summary", "Body", null, null, null, null, ["dotnet"]));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdatePost_WhenANewTagLosesItsRace_ReturnsConflict()
    {
        // UpdatePost inserts tags too, so it carries the same race as the create path.
        await using var factory = new SaveFailureFactory();
        var client = await AuthedClientAsync(factory, "dup_post_update");
        var created = await client.PostAsJsonAsync("/api/posts", new CreateBlogPostRequest(
            "Editable", "Summary", "Body", null, null, null, null, []));
        var post = (await created.Content.ReadFromJsonAsync<BlogPostDetailDto>())!;

        factory.Failures.NextFailure = SaveFailureSwitch.DuplicateKeyOnRelationalProvider();
        var response = await client.PutAsJsonAsync($"/api/posts/{post.Id}", new UpdateBlogPostRequest(
            post.Title, post.Summary, post.Content, null, null, null, null, ["fresh-tag"],
            Slug: post.Slug, ConcurrencyStamp: post.ConcurrencyStamp));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreatePage_WhenTheInsertLosesASlugRace_ReturnsConflict()
    {
        await using var factory = new SaveFailureFactory();
        var client = await AuthedClientAsync(factory, "dup_page");

        factory.Failures.NextFailure = SaveFailureSwitch.DuplicateKeyOnRelationalProvider();
        var response = await client.PostAsJsonAsync("/api/pages", new CreatePageRequest(
            "Contested page", "contested-page", "Content", null, null, null, null, false));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateUser_WhenTheInsertLosesAUsernameRace_ReturnsTheSameConflictAsThePreCheck()
    {
        // The pre-check already answers "Username already exists." for the non-racing case, so the
        // race arriving at the same answer is the whole point: the caller cannot tell which of the
        // two caught it, and does not need to.
        await using var factory = new SaveFailureFactory();
        var client = await AuthedClientAsync(factory, "dup_user");

        factory.Failures.NextFailure = SaveFailureSwitch.DuplicateKeyOnRelationalProvider();
        var response = await client.PostAsJsonAsync("/api/users", new CreateUserRequest(
            $"contested_{Guid.NewGuid():N}", "Contested User", "Password1!"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Username already exists");
    }

    [Fact]
    public async Task CreateUser_WithAUsernameThatAlreadyExists_StillReturnsConflictWithoutAnyRace()
    {
        // Guards the ordinary path against being lost to the new handling.
        await using var factory = new SaveFailureFactory();
        var client = await AuthedClientAsync(factory, "dup_user_plain");
        var username = $"taken_{Guid.NewGuid():N}";
        var request = new CreateUserRequest(username, "Taken User", "Password1!");

        (await client.PostAsJsonAsync("/api/users", request))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var second = await client.PostAsJsonAsync("/api/users", request);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static async Task<HttpClient> AuthedClientAsync(SaveFailureFactory factory, string prefix)
    {
        var username = $"{prefix}_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        return factory.CreateClient().WithAuth(userId, username);
    }
}
