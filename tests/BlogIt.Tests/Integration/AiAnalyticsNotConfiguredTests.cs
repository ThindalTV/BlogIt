using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Pins the documented degradation when no AI or analytics satellite package is installed. The
/// sample host in the Testing environment configures neither provider, so this exercises the
/// engine's real defaults over the real endpoints.
/// </summary>
/// <remarks>
/// The requirement these cover is that "not installed" is a deliberate response, not a
/// NullReferenceException or a DI activation failure: <c>IAiService</c> and
/// <c>IAnalyticsService</c> are endpoint handler parameters, so leaving them unregistered would
/// have produced an unhandled 500 with a container stack trace before any BlogIt error handling
/// ran.
/// </remarks>
public sealed class AiAnalyticsNotConfiguredTests(BlogItSampleFactory factory)
    : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task AnalyticsSummary_ReportsNotConfigured()
    {
        var userId = await factory.SeedUserAsync($"no-analytics-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var response = await client.GetAsync("/api/analytics/summary");

        // The same 404 a site gets with BlogIt.GoogleAnalytics installed but its property ID or
        // service-account JSON left blank, so the dashboard panel handles both identically.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("Analytics is not configured.");
    }

    [Fact]
    public async Task AiConversationCrud_KeepsWorkingWithoutAnAiProvider()
    {
        var userId = await factory.SeedUserAsync($"no-ai-crud-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations",
            new CreateAiConversationRequest("Notes with no provider"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var conversation = await create.Content.ReadFromJsonAsync<AiConversationDetailDto>();

        // Listing, reading and deleting touch only the database, so conversations brainstormed
        // before an AI package was removed stay readable and removable.
        var list = await client.GetFromJsonAsync<List<AiConversationSummaryDto>>(
            "/api/ai/conversations");
        list.Should().ContainSingle().Which.Id.Should().Be(conversation!.Id);
        (await client.GetAsync($"/api/ai/conversations/{conversation.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/ai/conversations/{conversation.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SendMessage_ReportsWhichPackageToInstall()
    {
        var userId = await factory.SeedUserAsync($"no-ai-send-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations",
            new CreateAiConversationRequest("Brainstorm with no provider"));
        var conversationId = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!.Id;

        var send = await client.PostAsJsonAsync(
            $"/api/ai/conversations/{conversationId}/messages",
            new SendAiMessageRequest("Write an outline"));

        // 400, not 501: this is the same response the admin already renders for a provider that is
        // installed but has no API key entered, so the AI screen needs no change to show it.
        send.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await send.Content.ReadAsStringAsync();
        body.Should().Contain("BlogIt.OpenAi");
        body.Should().Contain("UseOpenAi");
    }

    [Fact]
    public async Task ExportDraft_ReportsWhichPackageToInstall()
    {
        var userId = await factory.SeedUserAsync($"no-ai-export-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations",
            new CreateAiConversationRequest("Export with no provider"));
        var conversationId = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!.Id;

        var export = await client.PostAsJsonAsync(
            $"/api/ai/conversations/{conversationId}/export-draft",
            new ExportAiConversationRequest(null));

        export.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await export.Content.ReadAsStringAsync()).Should().Contain("BlogIt.OpenAi");
    }
}
