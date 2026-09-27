using System.Net;
using System.Net.Http.Headers;
using BlogIt.Admin.Services;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers the admin's single point of token handling. Before this handler existed the bearer token
/// was attached by a hand-called PrepareAuthAsync at the top of ~28 ApiClient methods and nothing
/// anywhere looked at a 401, so a revoked or expired token left the sidebar showing the username
/// and [Authorize] pages rendering while every request underneath failed.
/// </summary>
public class AdminAuthMessageHandlerTests
{
    private const string TokenKey = "blogit_token";

    private static (AdminAuthMessageHandler Handler,
                    HttpClient Client,
                    RecordingHttpMessageHandler Inner,
                    FakeBrowserJsRuntime Js,
                    RecordingNavigationManager Nav,
                    AuthStateProvider Auth) CreatePipeline(
        string? storedToken = "stored-token",
        string currentRelativeUri = "posts")
    {
        var js = new FakeBrowserJsRuntime();
        if (storedToken is not null)
            js.Storage[TokenKey] = storedToken;

        var auth = new AuthStateProvider(new LocalStorageService(js));
        var nav = new RecordingNavigationManager(currentRelativeUri);
        var inner = new RecordingHttpMessageHandler();
        var handler = new AdminAuthMessageHandler(auth, nav) { InnerHandler = inner };
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://blog.example/api/") };
        return (handler, client, inner, js, nav, auth);
    }

    [Fact]
    public async Task SendAsync_AttachesStoredTokenAsBearerHeader()
    {
        var pipeline = CreatePipeline(storedToken: "jwt-abc");

        await pipeline.Client.GetAsync("posts");

        pipeline.Inner.SingleRequest.Headers.Authorization.Should()
            .BeEquivalentTo(new AuthenticationHeaderValue("Bearer", "jwt-abc"));
    }

    [Fact]
    public async Task SendAsync_WithoutStoredToken_SendsNoAuthorizationHeader()
    {
        var pipeline = CreatePipeline(storedToken: null);

        await pipeline.Client.GetAsync("posts");

        pipeline.Inner.SingleRequest.Headers.Authorization.Should().BeNull();
    }

    [Fact]
    public async Task SendAsync_ReadsTokenPerRequest_SoParallelCallsCannotStealEachOther()
    {
        // The old PrepareAuthAsync mutated HttpClient.DefaultRequestHeaders, which is shared
        // state: Dashboard fires three list calls through Task.WhenAll, so an interleaved
        // logout or login could strip the header off a request already in flight. The header
        // now belongs to the request message.
        var pipeline = CreatePipeline(storedToken: "jwt-abc");

        var first = pipeline.Client.GetAsync("posts");
        var second = pipeline.Client.GetAsync("pages");
        await Task.WhenAll(first, second);

        pipeline.Inner.Requests.Should().HaveCount(2);
        pipeline.Inner.Requests.Should().OnlyContain(r =>
            r.Headers.Authorization != null && r.Headers.Authorization.Parameter == "jwt-abc");
    }

    [Fact]
    public async Task SendAsync_On401_ClearsStoredToken()
    {
        var pipeline = CreatePipeline();
        pipeline.Inner.Respond(HttpStatusCode.Unauthorized);

        await pipeline.Client.GetAsync("posts");

        pipeline.Js.Storage.Should().NotContainKey(TokenKey);
    }

    [Fact]
    public async Task SendAsync_On401_PublishesAnonymousAuthenticationState()
    {
        // The compounding half of the finding: GetAuthenticationStateAsync is called once and
        // cached until NotifyAuthenticationStateChanged fires, and nothing fired it except login
        // and logout.
        var pipeline = CreatePipeline();
        pipeline.Inner.Respond(HttpStatusCode.Unauthorized);
        AuthenticationState? published = null;
        pipeline.Auth.AuthenticationStateChanged += task => published = task.Result;

        await pipeline.Client.GetAsync("posts");

        published.Should().NotBeNull();
        published!.User.Identity?.IsAuthenticated.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_On401_RedirectsToLoginCarryingTheInterruptedPage()
    {
        var pipeline = CreatePipeline(currentRelativeUri: "posts/9f1c");
        pipeline.Inner.Respond(HttpStatusCode.Unauthorized);

        await pipeline.Client.GetAsync("posts/9f1c");

        pipeline.Nav.Navigations.Should().ContainSingle()
            .Which.Should().Be("login?returnUrl=posts%2F9f1c");
        pipeline.Nav.LastNavigationReplaced.Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_On401_NavigatesBeforePublishingAnonymousState()
    {
        // Order matters: publishing anonymous first re-renders the current [Authorize] page as
        // NotAuthorized, and RedirectToLogin then races this navigation with a bare "login" that
        // drops the returnUrl. Leaving the page first means that path never runs.
        var pipeline = CreatePipeline();
        pipeline.Inner.Respond(HttpStatusCode.Unauthorized);
        var order = new List<string>();
        pipeline.Auth.AuthenticationStateChanged += _ => order.Add("state");
        pipeline.Nav.LocationChanged += (_, _) => order.Add("navigation");

        await pipeline.Client.GetAsync("posts");

        order.Should().Equal("navigation", "state");
    }

    [Fact]
    public async Task SendAsync_On401_ReturnsTheResponseSoCallersStillSeeTheFailure()
    {
        var pipeline = CreatePipeline();
        pipeline.Inner.Respond(HttpStatusCode.Unauthorized);

        var response = await pipeline.Client.GetAsync("posts");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SendAsync_On401FromLogin_LeavesTheUserOnTheLoginPage()
    {
        // auth/login answers 401 for bad credentials, so treating it like a rejected session
        // would wipe storage and bounce the login page onto itself with returnUrl=login.
        var pipeline = CreatePipeline(storedToken: null, currentRelativeUri: "login");
        pipeline.Inner.Respond(HttpStatusCode.Unauthorized);

        await pipeline.Client.PostAsync("auth/login", null);

        pipeline.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_On403_KeepsTheSession()
    {
        // 403 means authenticated but not permitted — signing the user out would be wrong.
        var pipeline = CreatePipeline();
        pipeline.Inner.Respond(HttpStatusCode.Forbidden);

        await pipeline.Client.GetAsync("users");

        pipeline.Js.Storage.Should().ContainKey(TokenKey);
        pipeline.Nav.Navigations.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_OnSuccess_KeepsTheSessionAndDoesNotNavigate()
    {
        var pipeline = CreatePipeline();
        pipeline.Inner.Respond(HttpStatusCode.OK);

        await pipeline.Client.GetAsync("posts");

        pipeline.Js.Storage.Should().ContainKey(TokenKey);
        pipeline.Nav.Navigations.Should().BeEmpty();
    }
}
