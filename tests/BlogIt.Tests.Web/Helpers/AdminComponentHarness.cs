using System.Net;
using System.Text;
using System.Text.Json;
using BlogIt.Admin.Services;
using BlogIt.Shared.DTOs;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Helpers;

/// <summary>
/// bUnit context wired with the services every admin screen injects, so a test can render a real
/// page component and assert against the markup a browser would receive.
/// </summary>
/// <remarks>
/// The real <see cref="ApiClient"/> is used rather than a mock of it: these tests are about what a
/// screen renders for a given server reply, so the reply has to travel the actual deserialisation
/// path. Replies are matched by URL fragment instead of queued in order — several screens fire
/// concurrent loads (the dashboard runs three list calls through <c>Task.WhenAll</c>), and a queue
/// would make the fixture depend on which of them the runtime happened to start first.
/// <para>
/// JS interop runs loose. The components call out to EasyMDE, the clipboard and the modal focus
/// helpers; none of that can run in a headless renderer, and loose mode still records every call,
/// which is what lets the dialog tests assert that focus handling was asked for at all.
/// </para>
/// </remarks>
public class AdminComponentHarness : BunitContext
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly RoutingHttpMessageHandler _handler = new();

    /// <summary>Backing store for <see cref="LocalStorageService"/>, so a test can seed a token.</summary>
    public FakeBrowserJsRuntime BrowserStorage { get; } = new();

    public AdminComponentHarness()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var localStorage = new LocalStorageService(BrowserStorage);
        var authProvider = new AuthStateProvider(localStorage);

        Services.AddSingleton(localStorage);
        Services.AddSingleton(new ApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("https://blog.example/api/") }));
        Services.AddSingleton(authProvider);
        Services.AddSingleton<AuthenticationStateProvider>(authProvider);
    }

    /// <summary>
    /// Every request the rendered components made, in order, with its body. Needed for the
    /// assertions that are about what a screen <em>sent</em> rather than what it rendered — the
    /// concurrency token on an update is invisible in the markup, and a save that quietly skips its
    /// unpublish call looks identical on screen to one that made it.
    /// </summary>
    public IReadOnlyList<RecordedRequest> Requests => _handler.Recorded;

    /// <summary>One request the admin sent, as the fake server saw it.</summary>
    public sealed record RecordedRequest(HttpMethod Method, string Url, string Body);

    /// <summary>Answers any request whose URL contains <paramref name="urlFragment"/> with <paramref name="payload"/>.</summary>
    public AdminComponentHarness Route<T>(string urlFragment, T payload) =>
        RouteJson(urlFragment, JsonSerializer.Serialize(payload, Web));

    /// <summary>Answers any request whose URL contains <paramref name="urlFragment"/> with a raw JSON body.</summary>
    public AdminComponentHarness RouteJson(string urlFragment, string json)
    {
        _handler.Routes.Add((urlFragment, HttpStatusCode.OK, json));
        return this;
    }

    /// <summary>Answers any request whose URL contains <paramref name="urlFragment"/> with a bare status.</summary>
    public AdminComponentHarness RouteStatus(string urlFragment, HttpStatusCode status)
    {
        _handler.Routes.Add((urlFragment, status, "{}"));
        return this;
    }

    /// <summary>
    /// Answers any request whose URL contains <paramref name="urlFragment"/> with a failure status
    /// and a body — the shape needed to exercise a screen's handling of a server error message,
    /// which <see cref="RouteStatus"/> cannot express because it always sends an empty object.
    /// </summary>
    public AdminComponentHarness RouteJson(string urlFragment, HttpStatusCode status, string json)
    {
        _handler.Routes.Add((urlFragment, status, json));
        return this;
    }

    /// <summary>A one-item page, the shape every list screen needs to render rows at all.</summary>
    public static PagedResult<T> OnePage<T>(params T[] items) => new(items, items.Length, 1, 20);

    private sealed class RoutingHttpMessageHandler : HttpMessageHandler
    {
        public List<(string Fragment, HttpStatusCode Status, string Json)> Routes { get; } = [];

        public List<RecordedRequest> Recorded { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.PathAndQuery;

            // Read the body here rather than handing the test the HttpRequestMessage: the content is
            // disposed with the message once the handler returns, so a test reaching for it later
            // would find it gone.
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Recorded.Add(new RecordedRequest(request.Method, url, body));

            // First match wins, so a test can register a narrow route ahead of a broad one.
            var route = Routes.FirstOrDefault(r => url.Contains(r.Fragment, StringComparison.OrdinalIgnoreCase));

            // 404 rather than an empty 200 for anything unrouted: the ApiClient turns a 404 into
            // null, which every screen already handles, whereas "{}" deserialises into a DTO with
            // null collections and fails inside the component for reasons unrelated to the test.
            var response = route.Fragment is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("\"not routed\"", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(route.Status)
                {
                    Content = new StringContent(route.Json, Encoding.UTF8, "application/json")
                };

            response.RequestMessage = request;
            return response;
        }
    }
}
