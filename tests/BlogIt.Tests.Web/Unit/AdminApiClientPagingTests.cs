using System.Net;
using BlogIt.Admin.Services;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The list calls the admin uses for Pages and Media used to send no paging or search parameters at
/// all, so they silently took the server's first 20 rows and the screens filtered that window
/// client-side — item 21 was invisible and a search for a file outside the window said
/// "No media files found."
/// </summary>
public class AdminApiClientPagingTests
{
    private const string EmptyPage = """{"items":[],"totalCount":0,"page":1,"pageSize":20}""";

    private static (ApiClient Api, RecordingHttpMessageHandler Http) Create()
    {
        var http = new RecordingHttpMessageHandler();
        http.Respond(HttpStatusCode.OK, EmptyPage);
        var client = new HttpClient(http) { BaseAddress = new Uri("https://blog.example/api/") };
        return (new ApiClient(client), http);
    }

    private static string RequestPathAndQuery(RecordingHttpMessageHandler http)
        => http.SingleRequest.RequestUri!.PathAndQuery;

    [Fact]
    public async Task GetPagesAsync_RequestsTheAskedForPage()
    {
        var (api, http) = Create();

        await api.GetPagesAsync(page: 3);

        RequestPathAndQuery(http).Should().Be("/api/pages?page=3&pageSize=20");
    }

    [Fact]
    public async Task GetPagesAsync_DefaultsToTheFirstPage()
    {
        var (api, http) = Create();

        await api.GetPagesAsync();

        RequestPathAndQuery(http).Should().Be("/api/pages?page=1&pageSize=20");
    }

    [Fact]
    public async Task GetPagesAsync_PushesSearchToTheServer()
    {
        var (api, http) = Create();

        await api.GetPagesAsync(q: "about us");

        RequestPathAndQuery(http).Should().Be("/api/pages?page=1&pageSize=20&q=about%20us");
    }

    [Fact]
    public async Task GetMediaAsync_RequestsTheAskedForPage()
    {
        var (api, http) = Create();

        await api.GetMediaAsync(page: 4);

        RequestPathAndQuery(http).Should().Be("/api/media?page=4&pageSize=20");
    }

    [Fact]
    public async Task GetMediaAsync_PushesSearchToTheServer()
    {
        var (api, http) = Create();

        await api.GetMediaAsync(q: "logo&mark");

        RequestPathAndQuery(http).Should().Be("/api/media?page=1&pageSize=20&q=logo%26mark");
    }

    [Fact]
    public async Task GetMediaAsync_HonoursAnExplicitPageSize()
    {
        // The Dashboard only wants TotalCount, so it asks for the smallest possible window.
        var (api, http) = Create();

        await api.GetMediaAsync(pageSize: 1);

        RequestPathAndQuery(http).Should().Be("/api/media?page=1&pageSize=1");
    }

    [Fact]
    public async Task GetPostsAsync_KeepsSendingPageStatusAndSearch()
    {
        var (api, http) = Create();

        await api.GetPostsAsync(q: "draft note", page: 2, status: "draft");

        RequestPathAndQuery(http).Should()
            .Be("/api/posts?page=2&pageSize=20&q=draft%20note&status=draft");
    }
}
