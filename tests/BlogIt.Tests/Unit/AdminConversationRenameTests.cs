using System.Net;
using BlogIt.Admin.Services;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The chat header advertised "Click to rename" and opened an edit box whose save method was a
/// single comment saying there was no API — the typed title went nowhere. There is no bUnit in this
/// repo, so the component itself cannot be rendered; what is asserted here is the client call the
/// component now makes, and <c>AiApiTests</c> covers the endpoint on the other end of it.
/// </summary>
public class AdminConversationRenameTests
{
    private static (ApiClient Api, RecordingHttpMessageHandler Http) Create(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = "{}")
    {
        var http = new RecordingHttpMessageHandler();
        http.Respond(status, body);
        var client = new HttpClient(http) { BaseAddress = new Uri("https://blog.example/api/") };
        return (new ApiClient(client), http);
    }

    [Fact]
    public async Task RenameConversationAsync_PutsToTheTitleEndpoint()
    {
        var id = Guid.NewGuid();
        var (api, http) = Create();

        await api.RenameConversationAsync(id, "Q3 launch announcement");

        http.SingleRequest.Method.Should().Be(HttpMethod.Put);
        http.SingleRequest.RequestUri!.PathAndQuery.Should()
            .Be($"/api/ai/conversations/{id}/title");
    }

    [Fact]
    public async Task RenameConversationAsync_SendsTheTypedTitle()
    {
        var (api, http) = Create();

        await api.RenameConversationAsync(Guid.NewGuid(), "Renamed by the operator");

        var sent = await http.SingleRequest.Content!.ReadAsStringAsync();
        sent.Should().Be("""{"title":"Renamed by the operator"}""");
    }

    [Fact]
    public async Task RenameConversationAsync_ReturnsTheConversationTheServerSaved()
    {
        var id = Guid.NewGuid();
        var (api, _) = Create(HttpStatusCode.OK, $$"""
            {"id":"{{id}}","title":"Saved title","createdAt":"2026-01-01T00:00:00Z",
             "updatedAt":"2026-01-02T00:00:00Z","linkedDraftId":null,"messages":[]}
            """);

        var conversation = await api.RenameConversationAsync(id, "Saved title");

        conversation!.Title.Should().Be("Saved title");
    }

    [Fact]
    public async Task RenameConversationAsync_SurfacesAValidationRefusal()
    {
        var (api, _) = Create(
            HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","errors":{"title":["Title is required."]}}""");

        var act = () => api.RenameConversationAsync(Guid.NewGuid(), "   ");

        await act.Should().ThrowAsync<HttpRequestException>().WithMessage("Title is required.");
    }
}
