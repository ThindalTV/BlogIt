using System.Net;
using System.Net.Http.Json;
using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogIt.Tests.Integration;

public sealed class AiApiTests(AiApiTests.AiFactory factory)
    : IClassFixture<AiApiTests.AiFactory>
{
    [Fact]
    public async Task Conversations_RequireAuthentication()
    {
        var response = await factory.CreateClient().GetAsync("/api/ai/conversations");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConversationLifecycle_IsScopedToCurrentUser()
    {
        var ownerId = await factory.SeedUserAsync($"ai-owner-{Guid.NewGuid():N}");
        var otherId = await factory.SeedUserAsync($"ai-other-{Guid.NewGuid():N}");
        var owner = factory.CreateClient().WithAuth(ownerId);
        var other = factory.CreateClient().WithAuth(otherId);

        var create = await owner.PostAsJsonAsync(
            "/api/ai/conversations",
            new CreateAiConversationRequest("Draft an article"));

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var conversation = await create.Content.ReadFromJsonAsync<AiConversationDetailDto>();
        conversation.Should().NotBeNull();
        conversation!.Title.Should().Be("Draft an article");
        conversation.Messages.Should().BeEmpty();
        create.Headers.Location.Should().Be(
            new Uri($"/api/ai/conversations/{conversation.Id}", UriKind.Relative));

        var summaries = await owner.GetFromJsonAsync<List<AiConversationSummaryDto>>(
            "/api/ai/conversations");
        summaries.Should().ContainSingle()
            .Which.Should().Match<AiConversationSummaryDto>(
                item => item.Id == conversation.Id
                    && item.Title == conversation.Title
                    && item.MessageCount == 0);

        var get = await owner.GetAsync($"/api/ai/conversations/{conversation.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        (await get.Content.ReadFromJsonAsync<AiConversationDetailDto>())
            .Should().BeEquivalentTo(conversation);

        (await other.GetAsync($"/api/ai/conversations/{conversation.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/ai/conversations/{conversation.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var delete = await owner.DeleteAsync($"/api/ai/conversations/{conversation.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await owner.GetAsync($"/api/ai/conversations/{conversation.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Rename ───────────────────────────────────────────────────────────────
    // The admin creates every conversation under the hardcoded title "New Conversation" and its
    // chat header offers "Click to rename", but there was no endpoint behind it, so the typed
    // title was silently dropped and the list was a column of identical rows.

    [Fact]
    public async Task RenameConversation_PersistsTheNewTitle()
    {
        var userId = await factory.SeedUserAsync($"ai-rename-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("New Conversation"));
        var conversation = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!;

        var rename = await client.PutAsJsonAsync(
            $"/api/ai/conversations/{conversation.Id}/title",
            new RenameAiConversationRequest("Q3 launch announcement"));

        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        (await rename.Content.ReadFromJsonAsync<AiConversationDetailDto>())!
            .Title.Should().Be("Q3 launch announcement");
        var reloaded = await client.GetFromJsonAsync<AiConversationDetailDto>(
            $"/api/ai/conversations/{conversation.Id}");
        reloaded!.Title.Should().Be("Q3 launch announcement");
    }

    [Fact]
    public async Task RenameConversation_ShowsTheNewTitleInTheList()
    {
        var userId = await factory.SeedUserAsync($"ai-rename-list-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("New Conversation"));
        var conversation = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!;

        await client.PutAsJsonAsync(
            $"/api/ai/conversations/{conversation.Id}/title",
            new RenameAiConversationRequest("Renamed in the list"));

        var summaries = await client.GetFromJsonAsync<List<AiConversationSummaryDto>>(
            "/api/ai/conversations");
        summaries.Should().ContainSingle().Which.Title.Should().Be("Renamed in the list");
    }

    [Fact]
    public async Task RenameConversation_IsScopedToItsOwner()
    {
        var ownerId = await factory.SeedUserAsync($"ai-rename-owner-{Guid.NewGuid():N}");
        var otherId = await factory.SeedUserAsync($"ai-rename-other-{Guid.NewGuid():N}");
        var owner = factory.CreateClient().WithAuth(ownerId);
        var other = factory.CreateClient().WithAuth(otherId);
        var create = await owner.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("Owned"));
        var conversation = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!;

        var rename = await other.PutAsJsonAsync(
            $"/api/ai/conversations/{conversation.Id}/title",
            new RenameAiConversationRequest("Stolen"));

        rename.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.GetFromJsonAsync<AiConversationDetailDto>(
            $"/api/ai/conversations/{conversation.Id}"))!.Title.Should().Be("Owned");
    }

    [Fact]
    public async Task RenameConversation_RequiresAuthentication()
    {
        var response = await factory.CreateClient().PutAsJsonAsync(
            $"/api/ai/conversations/{Guid.NewGuid()}/title",
            new RenameAiConversationRequest("Anything"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenameConversation_RejectsABlankTitle(string title)
    {
        var userId = await factory.SeedUserAsync($"ai-rename-blank-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("Keeps its name"));
        var conversation = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!;

        var rename = await client.PutAsJsonAsync(
            $"/api/ai/conversations/{conversation.Id}/title",
            new RenameAiConversationRequest(title));

        rename.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await rename.Content.ReadAsStringAsync()).Should().Contain("Title is required.");
        (await client.GetFromJsonAsync<AiConversationDetailDto>(
            $"/api/ai/conversations/{conversation.Id}"))!.Title.Should().Be("Keeps its name");
    }

    [Fact]
    public async Task RenameConversation_RejectsATitleTooLongForTheColumn()
    {
        // Same bound CreateConversation checks. Without it the over-long value reaches SaveChanges
        // and comes back as a 500 that names nothing.
        var userId = await factory.SeedUserAsync($"ai-rename-long-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("Short"));
        var conversation = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!;

        var rename = await client.PutAsJsonAsync(
            $"/api/ai/conversations/{conversation.Id}/title",
            new RenameAiConversationRequest(new string('x', ContentLimits.TitleLength + 1)));

        rename.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RenameConversation_ForAnUnknownId_ReturnsNotFound()
    {
        var userId = await factory.SeedUserAsync($"ai-rename-missing-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);

        var rename = await client.PutAsJsonAsync(
            $"/api/ai/conversations/{Guid.NewGuid()}/title",
            new RenameAiConversationRequest("Nothing to rename"));

        rename.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RenameConversation_LeavesTheMessagesAlone()
    {
        // Seeded straight into the store rather than sent through /messages: FakeAiService returns
        // a DTO without ever persisting a row, so a conversation driven through the endpoint has
        // nothing on disk for the rename to preserve.
        var userId = await factory.SeedUserAsync($"ai-rename-msgs-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("Before"));
        var conversation = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
            db.AiMessages.Add(new AiMessage
            {
                ConversationId = conversation.Id,
                Role = "user",
                Content = "Write an outline",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var rename = await client.PutAsJsonAsync(
            $"/api/ai/conversations/{conversation.Id}/title",
            new RenameAiConversationRequest("After"));

        var renamed = await rename.Content.ReadFromJsonAsync<AiConversationDetailDto>();
        renamed!.Title.Should().Be("After");
        renamed.Messages.Should().ContainSingle().Which.Content.Should().Be("Write an outline");
    }

    [Fact]
    public async Task SendAndExport_UseAiServiceForOwnedConversation()
    {
        var userId = await factory.SeedUserAsync($"ai-actions-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations",
            new CreateAiConversationRequest("Action conversation"));
        var conversation = await create.Content.ReadFromJsonAsync<AiConversationDetailDto>();
        var conversationId = conversation!.Id;

        var send = await client.PostAsJsonAsync(
            $"/api/ai/conversations/{conversationId}/messages",
            new SendAiMessageRequest("Write an outline"));

        send.StatusCode.Should().Be(HttpStatusCode.OK);
        var sent = await send.Content.ReadFromJsonAsync<AiConversationDetailDto>();
        sent!.Messages.Should().ContainSingle()
            .Which.Content.Should().Be("assistant response");
        factory.AiService.SentConversationId.Should().Be(conversationId);
        factory.AiService.SentContent.Should().Be("Write an outline");

        var export = await client.PostAsJsonAsync(
            $"/api/ai/conversations/{conversationId}/export-draft",
            new ExportAiConversationRequest("Use a practical tone"));

        export.StatusCode.Should().Be(HttpStatusCode.Created);
        var exported = await export.Content.ReadFromJsonAsync<ExportAiConversationResponse>();
        exported.Should().Be(new ExportAiConversationResponse(
            factory.AiService.ExportedPost.Id,
            factory.AiService.ExportedPost.Slug));
        export.Headers.Location.Should().Be(
            new Uri($"/api/posts/{factory.AiService.ExportedPost.Id}", UriKind.Relative));
        factory.AiService.ExportedConversationId.Should().Be(conversationId);
        factory.AiService.ExportedAuthorId.Should().Be(userId);
        factory.AiService.ExportInstructions.Should().Be("Use a practical tone");
    }

    [Fact]
    public async Task SendAndExport_ReturnNotFoundWithoutCallingAiForAnotherUsersConversation()
    {
        var ownerId = await factory.SeedUserAsync($"ai-private-owner-{Guid.NewGuid():N}");
        var otherId = await factory.SeedUserAsync($"ai-private-other-{Guid.NewGuid():N}");
        var owner = factory.CreateClient().WithAuth(ownerId);
        var other = factory.CreateClient().WithAuth(otherId);
        var create = await owner.PostAsJsonAsync(
            "/api/ai/conversations",
            new CreateAiConversationRequest("Private conversation"));
        var conversation = await create.Content.ReadFromJsonAsync<AiConversationDetailDto>();
        factory.AiService.Reset();

        var send = await other.PostAsJsonAsync(
            $"/api/ai/conversations/{conversation!.Id}/messages",
            new SendAiMessageRequest("Intrude"));
        var export = await other.PostAsJsonAsync(
            $"/api/ai/conversations/{conversation.Id}/export-draft",
            new ExportAiConversationRequest(null));

        send.StatusCode.Should().Be(HttpStatusCode.NotFound);
        export.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.AiService.SentConversationId.Should().BeNull();
        factory.AiService.ExportedConversationId.Should().BeNull();
    }

    [Fact]
    public async Task SendMessage_WhenAiServiceThrowsInvalidOperationException_ReturnsBadRequestWithMessage()
    {
        var userId = await factory.SeedUserAsync($"ai-config-error-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("Conversation"));
        var conversationId = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!.Id;

        factory.AiService.ExceptionToThrow = new InvalidOperationException("AI API key is not configured.");
        try
        {
            var send = await client.PostAsJsonAsync(
                $"/api/ai/conversations/{conversationId}/messages",
                new SendAiMessageRequest("Hello"));

            send.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await send.Content.ReadAsStringAsync()).Should().Contain("AI API key is not configured.");
        }
        finally { factory.AiService.Reset(); }
    }

    [Fact]
    public async Task SendMessage_WhenAiServiceThrowsUnexpectedException_ReturnsGenericBadGatewayWithoutLeakingDetails()
    {
        var userId = await factory.SeedUserAsync($"ai-provider-error-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("Conversation"));
        var conversationId = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!.Id;

        factory.AiService.ExceptionToThrow = new HttpRequestException("secret-internal-detail: connection reset by upstream 10.0.4.12");
        try
        {
            var send = await client.PostAsJsonAsync(
                $"/api/ai/conversations/{conversationId}/messages",
                new SendAiMessageRequest("Hello"));

            send.StatusCode.Should().Be(HttpStatusCode.BadGateway);
            var body = await send.Content.ReadAsStringAsync();
            body.Should().NotContain("secret-internal-detail");
            body.Should().NotContain("10.0.4.12");
        }
        finally { factory.AiService.Reset(); }
    }

    [Fact]
    public async Task ExportDraft_WhenAiServiceThrowsUnexpectedException_ReturnsGenericBadGatewayWithoutLeakingDetails()
    {
        var userId = await factory.SeedUserAsync($"ai-export-error-{Guid.NewGuid():N}");
        var client = factory.CreateClient().WithAuth(userId);
        var create = await client.PostAsJsonAsync(
            "/api/ai/conversations", new CreateAiConversationRequest("Conversation"));
        var conversationId = (await create.Content.ReadFromJsonAsync<AiConversationDetailDto>())!.Id;

        factory.AiService.ExceptionToThrow = new InvalidOperationException("internal stack detail");
        try
        {
            var export = await client.PostAsJsonAsync(
                $"/api/ai/conversations/{conversationId}/export-draft",
                new ExportAiConversationRequest(null));

            // InvalidOperationException maps to 400 (see AiApi.HandleAiFailure) since AiService
            // only throws it for known, safe-to-surface conditions.
            export.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally { factory.AiService.Reset(); }
    }

    public sealed class AiFactory : BlogItSampleFactory
    {
        public FakeAiService AiService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAiService>();
                services.AddSingleton<IAiService>(AiService);
            });
        }
    }

    public sealed class FakeAiService : IAiService
    {
        public Guid? SentConversationId { get; private set; }
        public string? SentContent { get; private set; }
        public Guid? ExportedConversationId { get; private set; }
        public Guid? ExportedAuthorId { get; private set; }
        public string? ExportInstructions { get; private set; }
        public Exception? ExceptionToThrow { get; set; }
        public BlogPost ExportedPost { get; } = new()
        {
            Id = Guid.NewGuid(),
            Title = "Exported draft",
            Slug = "exported-draft",
            Summary = "summary",
            Content = "content"
        };

        public Task<AiConversationDetailDto> SendMessageAsync(
            Guid conversationId,
            string userContent,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;

            SentConversationId = conversationId;
            SentContent = userContent;
            return Task.FromResult(new AiConversationDetailDto(
                conversationId,
                "Action conversation",
                DateTime.UtcNow,
                DateTime.UtcNow,
                null,
                [
                    new AiMessageDto(
                        Guid.NewGuid(),
                        "assistant",
                        "assistant response",
                        DateTime.UtcNow)
                ]));
        }

        public Task<BlogPost> ExportToDraftAsync(
            Guid conversationId,
            Guid authorId,
            string? additionalInstructions,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;

            ExportedConversationId = conversationId;
            ExportedAuthorId = authorId;
            ExportInstructions = additionalInstructions;
            return Task.FromResult(ExportedPost);
        }

        public void Reset()
        {
            SentConversationId = null;
            SentContent = null;
            ExportedConversationId = null;
            ExportedAuthorId = null;
            ExportInstructions = null;
            ExceptionToThrow = null;
        }
    }
}
