using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace BlogIt.Tests.Helpers;

/// <summary>
/// Terminal <see cref="HttpMessageHandler"/> that records every request it is handed and replies
/// with canned responses. Used instead of Moq for the admin HTTP tests because the assertions are
/// about the request that came out the far side of the pipeline, which a recorder states plainly.
/// </summary>
public sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    /// <summary>Every request that reached this handler, oldest first.</summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>The single request that reached this handler. Throws when there was not exactly one.</summary>
    public HttpRequestMessage SingleRequest => Requests.Single();

    /// <summary>Queues the next reply. Falls back to 200 with an empty JSON object when empty.</summary>
    public RecordingHttpMessageHandler Respond(HttpStatusCode status, string json = "{}")
        => Respond(status, json, "application/json");

    /// <summary>
    /// Queues the next reply with an explicit media type. Needed to reproduce the bodies that are
    /// not one of the API's own error shapes — a proxy's HTML page, or a reply with no body at all
    /// (<paramref name="mediaType"/> null) — which the client has to tell apart from real JSON.
    /// </summary>
    public RecordingHttpMessageHandler Respond(HttpStatusCode status, string body, string? mediaType)
    {
        _responses.Enqueue(new HttpResponseMessage(status)
        {
            Content = mediaType is null
                ? new StringContent("")  { Headers = { ContentType = null } }
                : new StringContent(body, Encoding.UTF8, mediaType)
        });
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        response.RequestMessage = request;
        return Task.FromResult(response);
    }
}

/// <summary>
/// In-memory stand-in for the browser's localStorage, driving the real
/// <c>LocalStorageService</c> so the tests exercise its actual JS call shapes rather than a
/// hand-written substitute for it.
/// </summary>
public sealed class FakeBrowserJsRuntime : IJSRuntime
{
    /// <summary>The backing store, exposed so tests can seed and inspect it.</summary>
    public Dictionary<string, string> Storage { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(
        string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        var key = args?.Length > 0 ? args[0]?.ToString() ?? "" : "";
        switch (identifier)
        {
            case "localStorage.getItem":
                var value = Storage.TryGetValue(key, out var stored) ? stored : null;
                return ValueTask.FromResult((TValue)(object?)value!);
            case "localStorage.setItem":
                Storage[key] = args?.Length > 1 ? args[1]?.ToString() ?? "" : "";
                break;
            case "localStorage.removeItem":
                Storage.Remove(key);
                break;
        }

        // InvokeVoidAsync lands here as InvokeAsync<IJSVoidResult>; default(TValue) is the
        // only legal answer and the caller discards it.
        return ValueTask.FromResult(default(TValue)!);
    }
}

/// <summary>
/// <see cref="NavigationManager"/> that records navigations instead of performing them.
/// <see cref="NavigationManager.NavigateTo(string,bool)"/> is not virtual, so intercepting it
/// means overriding <c>NavigateToCore</c> — hence a real subclass rather than a mock.
/// </summary>
public sealed class RecordingNavigationManager : NavigationManager
{
    public RecordingNavigationManager(string relativeUri = "")
        => Initialize("https://blog.example/blogit/", $"https://blog.example/blogit/{relativeUri}");

    /// <summary>Every URI passed to NavigateTo, oldest first.</summary>
    public List<string> Navigations { get; } = [];

    /// <summary>True when the most recent navigation replaced the history entry.</summary>
    public bool LastNavigationReplaced { get; private set; }

    protected override void NavigateToCore(string uri, bool forceLoad)
        => NavigateToCore(uri, new NavigationOptions { ForceLoad = forceLoad });

    protected override void NavigateToCore(string uri, NavigationOptions options)
    {
        Navigations.Add(uri);
        LastNavigationReplaced = options.ReplaceHistoryEntry;
        Uri = ToAbsoluteUri(uri).ToString();
        // Raised so tests can observe navigation ordering relative to other events the way a
        // rendered app would.
        NotifyLocationChanged(isInterceptedLink: false);
    }
}
