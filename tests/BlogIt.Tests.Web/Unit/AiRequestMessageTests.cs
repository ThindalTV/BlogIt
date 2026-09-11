using BlogIt.Services;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Pins the size of the request OpenAiService sends to the model for one chat turn. AiApiTests
/// substitutes a fake IAiService, so nothing there can see this — a duplicated user message rode
/// on every turn undetected, doubling the token bill and firing history compaction a message
/// early, while AiHistoryCompactionTests kept passing because it tests the batch policy on a
/// list it builds itself.
/// </summary>
/// <remarks>
/// Reaches OpenAiService (in the BlogIt.OpenAi satellite package) through its InternalsVisibleTo;
/// the type was called AiService and lived in the core package until the AI SDK was split out.
/// </remarks>
public class AiRequestMessageTests
{
    [Fact]
    public async Task History_ContainsTheJustAddedUserMessageExactlyOnce()
    {
        await using var db = CreateContext();
        var conversation = await SeedAndLoadAsync(db, priorMessages: 2);

        var userMessage = AddUserTurn(db, conversation.Id);
        await db.SaveChangesAsync();

        var ordered = OpenAiService.SelectHistoryForRequest(conversation);

        // The conversation was loaded tracked with Include(c => c.Messages), so EF relationship
        // fixup has already placed userMessage into that collection — appending it as well is
        // what sent the user's turn to the provider twice.
        ordered.Should().HaveCount(3);
        ordered.Count(message => ReferenceEquals(message, userMessage)).Should().Be(1);
        ordered.Select(message => message.Content).Should().Equal("prior-0", "prior-1", "the new turn");
    }

    [Fact]
    public async Task OutboundRequest_IsOneMessagePerHistoryEntry()
    {
        await using var db = CreateContext();
        var conversation = await SeedAndLoadAsync(db, priorMessages: 4);

        AddUserTurn(db, conversation.Id);
        await db.SaveChangesAsync();

        var ordered = OpenAiService.SelectHistoryForRequest(conversation);
        var outbound = OpenAiService.BuildRequestMessages(conversation.Summary, ordered);

        outbound.Should().HaveCount(5);
    }

    [Fact]
    public async Task OutboundRequest_PrependsOneSystemMessageWhenASummaryExists()
    {
        await using var db = CreateContext();
        var conversation = await SeedAndLoadAsync(db, priorMessages: 2, summary: "what came before");

        AddUserTurn(db, conversation.Id);
        await db.SaveChangesAsync();

        var ordered = OpenAiService.SelectHistoryForRequest(conversation);
        var outbound = OpenAiService.BuildRequestMessages(conversation.Summary, ordered);

        outbound.Should().HaveCount(ordered.Count + 1);
    }

    [Fact]
    public async Task History_StopsOneShortOfTheCompactionThreshold()
    {
        // The off-by-one the duplicate caused: with threshold-1 messages already stored, the new
        // turn brings the history to exactly threshold-1 + 1, and compaction must not fire a turn
        // before that. An extra copy of the user message pushed this over the line early.
        await using var db = CreateContext();
        var conversation = await SeedAndLoadAsync(
            db,
            priorMessages: OpenAiService.HistoryCompactionThreshold - 2);

        AddUserTurn(db, conversation.Id);
        await db.SaveChangesAsync();

        var ordered = OpenAiService.SelectHistoryForRequest(conversation);
        var (toCompact, _) = OpenAiService.SelectCompactionBatch(ordered, OpenAiService.HistoryCompactionThreshold);

        ordered.Should().HaveCount(OpenAiService.HistoryCompactionThreshold - 1);
        toCompact.Should().BeEmpty();
    }

    [Fact]
    public async Task History_ExcludesAlreadyCompactedMessages()
    {
        await using var db = CreateContext();
        var conversation = await SeedAndLoadAsync(db, priorMessages: 3, compactedCount: 2);

        AddUserTurn(db, conversation.Id);
        await db.SaveChangesAsync();

        var ordered = OpenAiService.SelectHistoryForRequest(conversation);

        ordered.Select(message => message.Content).Should().Equal("prior-2", "the new turn");
    }

    private static AiMessage AddUserTurn(BlogItDbContext db, Guid conversationId)
    {
        var userMessage = new AiMessage
        {
            ConversationId = conversationId,
            Role = "user",
            Content = "the new turn",
            CreatedAt = DateTime.UnixEpoch.AddYears(1)
        };
        db.AiMessages.Add(userMessage);
        return userMessage;
    }

    private static async Task<AiConversation> SeedAndLoadAsync(
        BlogItDbContext db,
        int priorMessages,
        string? summary = null,
        int compactedCount = 0)
    {
        var user = new AppUser
        {
            Username = $"author-{Guid.NewGuid():N}",
            DisplayName = "Author",
            PasswordHash = "unused"
        };
        var conversation = new AiConversation
        {
            Title = "Brainstorm",
            CreatedByUserId = user.Id,
            CreatedByUser = user,
            Summary = summary
        };
        db.AiConversations.Add(conversation);
        for (var i = 0; i < priorMessages; i++)
        {
            db.AiMessages.Add(new AiMessage
            {
                ConversationId = conversation.Id,
                Role = i % 2 == 0 ? "user" : "assistant",
                Content = $"prior-{i}",
                CreatedAt = DateTime.UnixEpoch.AddMinutes(i),
                IsCompacted = i < compactedCount
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // Loaded exactly the way SendMessageAsync loads it — tracked, with Messages included.
        return await db.AiConversations
            .Include(c => c.Messages)
            .FirstAsync(c => c.Id == conversation.Id);
    }

    private static BlogItDbContext CreateContext() => new(
        new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"AiRequest_{Guid.NewGuid():N}")
            .Options);
}
