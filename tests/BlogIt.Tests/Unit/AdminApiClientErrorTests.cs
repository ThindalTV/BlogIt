using System.Net;
using BlogIt.Admin.Services;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Roughly half the mutating calls on <see cref="ApiClient"/> used raw
/// <c>EnsureSuccessStatusCode()</c>, so a refusal the server had spelled out reached the operator as
/// "Response status code does not indicate success: 409 (Conflict)". The messages that were being
/// thrown away are the ones that say what to do next — a concurrency conflict tells you to reload,
/// and a rejected user delete names the content that blocks it.
/// </summary>
public class AdminApiClientErrorTests
{
    // The exact bodies the server produces. ConcurrencyGuard and UsersApi both reply with
    // Results.Conflict(string), which serialises as a bare JSON string.
    private const string ConcurrencyBody =
        """"
        "This content was changed by someone else after you loaded it. Reload to see the current version, then reapply your changes."
        """";

    private const string UserOwnsContentBody =
        """"
        "This user still owns 3 posts, 1 media file. Reassign or delete that content first."
        """";

    private static ApiClient RespondingWith(
        HttpStatusCode status,
        string body,
        string? mediaType = "application/json")
    {
        var http = new RecordingHttpMessageHandler();
        http.Respond(status, body, mediaType);
        return new ApiClient(new HttpClient(http) { BaseAddress = new Uri("https://blog.example/api/") });
    }

    // ── The thirteen call sites that discarded the body ──────────────────────

    public static TheoryData<string, Func<ApiClient, Task>> MutatingCalls => new()
    {
        { "DeletePostAsync", api => api.DeletePostAsync(Guid.NewGuid()) },
        { "PublishPostAsync", api => api.PublishPostAsync(Guid.NewGuid()) },
        { "UnpublishPostAsync", api => api.UnpublishPostAsync(Guid.NewGuid()) },
        { "CreatePostPreviewAsync", api => api.CreatePostPreviewAsync(Guid.NewGuid()) },
        { "DeletePageAsync", api => api.DeletePageAsync(Guid.NewGuid()) },
        { "CreatePagePreviewAsync", api => api.CreatePagePreviewAsync(Guid.NewGuid()) },
        { "UploadMediaAsync", api => api.UploadMediaAsync("t", new MemoryStream([1, 2]), "a.png", "image/png") },
        { "DeleteMediaAsync", api => api.DeleteMediaAsync(Guid.NewGuid()) },
        { "DeleteUserAsync", api => api.DeleteUserAsync(Guid.NewGuid()) },
        { "DeleteRedirectAsync", api => api.DeleteRedirectAsync(Guid.NewGuid()) },
        { "CreateConversationAsync", api => api.CreateConversationAsync(new CreateAiConversationRequest("t")) },
        { "DeleteConversationAsync", api => api.DeleteConversationAsync(Guid.NewGuid()) },
        { "RenameConversationAsync", api => api.RenameConversationAsync(Guid.NewGuid(), "t") },
        { "GetAnalyticsSummaryAsync", api => api.GetAnalyticsSummaryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow) },
    };

    [Theory]
    [MemberData(nameof(MutatingCalls))]
    public async Task EveryMutatingCall_SurfacesTheServersOwnMessage(
        string name, Func<ApiClient, Task> call)
    {
        var api = RespondingWith(HttpStatusCode.Conflict, ConcurrencyBody);

        var act = () => call(api);

        (await act.Should().ThrowAsync<HttpRequestException>().WithMessage(
            "This content was changed by someone else after you loaded it.*"))
            .Which.StatusCode.Should().Be(HttpStatusCode.Conflict, $"{name} should keep the status");
    }

    [Theory]
    [MemberData(nameof(MutatingCalls))]
    public async Task EveryMutatingCall_NeverLeaksTheFrameworksDefaultText(
        string name, Func<ApiClient, Task> call)
    {
        var api = RespondingWith(HttpStatusCode.Conflict, ConcurrencyBody);

        var act = () => call(api);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.Message.Should().NotContain("does not indicate success", $"{name} regressed");
    }

    [Fact]
    public async Task DeleteUserAsync_NamesTheContentThatBlocksTheDelete()
    {
        var api = RespondingWith(HttpStatusCode.Conflict, UserOwnsContentBody);

        var act = () => api.DeleteUserAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("This user still owns 3 posts, 1 media file.*");
    }

    // ── Error-body shapes this API actually produces ─────────────────────────

    [Fact]
    public async Task ValidationProblem_SurfacesTheFirstFieldMessage()
    {
        // Results.ValidationProblem — the shape the create/update endpoints use.
        var api = RespondingWith(
            HttpStatusCode.BadRequest,
            """{"type":"...","title":"One or more validation errors occurred.","status":400,"errors":{"title":["Title is required."]}}""");

        var act = () => api.CreatePostAsync(new CreateBlogPostRequest(
            "", "", null, null, null, null, null, []));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Title is required.");
    }

    [Fact]
    public async Task ValidationProblem_PrefersAFieldMessageOverTheGenericTitle()
    {
        // "One or more validation errors occurred." is the title on every ValidationProblem, so
        // reading it instead of the field message would tell the operator nothing.
        var api = RespondingWith(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"slug":["Slug is already in use."]}}""");

        var act = () => api.UpdatePageAsync(Guid.NewGuid(), new UpdatePageRequest(
            "t", "s", "c", null, null, null, null, IsPublished: false));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Slug is already in use.");
    }

    [Fact]
    public async Task PlainJsonString_IsSurfacedAsIs()
    {
        // Results.BadRequest(string) / Results.Conflict(string).
        var api = RespondingWith(HttpStatusCode.BadRequest, "\"Current password is incorrect.\"");

        var act = () => api.ChangePasswordAsync(new ChangePasswordRequest("a", "b"));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Current password is incorrect.");
    }

    [Fact]
    public async Task ProblemDetail_IsPreferredOverItsTitle()
    {
        // Results.Problem(message, statusCode) — what the AI endpoints return.
        var api = RespondingWith(
            HttpStatusCode.BadRequest,
            """{"title":"Bad Request","status":400,"detail":"No AI API key is configured."}""");

        var act = () => api.SendMessageAsync(Guid.NewGuid(), new SendAiMessageRequest("hi"));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("No AI API key is configured.");
    }

    [Fact]
    public async Task ProblemWithOnlyATitle_FallsBackToTheTitle()
    {
        var api = RespondingWith(HttpStatusCode.BadGateway, """{"title":"The AI request failed.","status":502}""");

        var act = () => api.SendMessageAsync(Guid.NewGuid(), new SendAiMessageRequest("hi"));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("The AI request failed.");
    }

    // ── Bodies that are not a message ────────────────────────────────────────

    [Fact]
    public async Task NoBodyAtAll_FallsBackToTheStatusCode()
    {
        // Results.Unauthorized() and the login rate limiter both reply with nothing.
        var api = RespondingWith(HttpStatusCode.TooManyRequests, "", mediaType: null);

        var act = () => api.DeletePostAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Request failed (429).");
    }

    [Fact]
    public async Task HtmlBody_IsNotPastedIntoTheMessage()
    {
        // A 500 from outside the endpoints — the developer exception page, or a proxy — is HTML.
        // Dumping a whole page into an admin alert is worse than reporting the status alone.
        var api = RespondingWith(
            HttpStatusCode.InternalServerError,
            "<html><body><h1>Unhandled exception</h1><pre>at BlogIt...</pre></body></html>",
            "text/html");

        var act = () => api.DeleteMediaAsync(Guid.NewGuid());

        (await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("Request failed (500)."))
            .Which.Message.Should().NotContain("<html>");
    }

    [Fact]
    public async Task MalformedJsonBody_FallsBackToTheStatusCode()
    {
        var api = RespondingWith(HttpStatusCode.Conflict, "{not json at all");

        var act = () => api.DeletePageAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Request failed (409).");
    }

    // ── Analytics keeps its "not installed" answer ───────────────────────────

    [Fact]
    public async Task GetAnalyticsSummaryAsync_StillTreatsNotFoundAsNotInstalled()
    {
        // 404 means the analytics endpoints are not mapped, which is a supported configuration —
        // it must stay a null, not become an error banner.
        var api = RespondingWith(HttpStatusCode.NotFound, "");

        var summary = await api.GetAnalyticsSummaryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);

        summary.Should().BeNull();
    }

    // ── Login stops calling every failure a bad password ─────────────────────

    [Fact]
    public async Task LoginAsync_OnRejectedCredentials_ReturnsNull()
    {
        var api = RespondingWith(HttpStatusCode.Unauthorized, "", mediaType: null);

        var response = await api.LoginAsync(new LoginRequest("admin", "wrong"));

        response.Should().BeNull();
    }

    [Fact]
    public async Task LoginAsync_OnServerError_ThrowsInsteadOfLookingLikeABadPassword()
    {
        // The old code returned null for every non-2xx, so the login screen told the operator their
        // password was wrong when the real cause was a broken server.
        var api = RespondingWith(HttpStatusCode.InternalServerError, "\"The signing key is missing.\"");

        var act = () => api.LoginAsync(new LoginRequest("admin", "right"));

        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("The signing key is missing.");
    }

    [Fact]
    public async Task LoginAsync_WhenRateLimited_ThrowsInsteadOfReturningNull()
    {
        var api = RespondingWith(HttpStatusCode.TooManyRequests, "", mediaType: null);

        var act = () => api.LoginAsync(new LoginRequest("admin", "right"));

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task LoginAsync_OnSuccess_StillReturnsTheToken()
    {
        var api = RespondingWith(
            HttpStatusCode.OK,
            """{"token":"jwt-value","username":"admin","displayName":"Admin","expiresAt":"2030-01-01T00:00:00Z"}""");

        var response = await api.LoginAsync(new LoginRequest("admin", "right"));

        response!.Token.Should().Be("jwt-value");
    }
}
