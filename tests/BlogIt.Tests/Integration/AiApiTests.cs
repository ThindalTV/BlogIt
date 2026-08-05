using System.Net;
using System.Net.Http.Json;
using BlogIt.Services;
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
        }
    }
}
