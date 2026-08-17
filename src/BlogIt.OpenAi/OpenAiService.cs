using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;

namespace BlogIt;

/// <summary>
/// The <see cref="IAiService"/> implementation backed by the official OpenAI .NET client. Covers
/// any OpenAI-compatible endpoint as well as GitHub Copilot's Azure-hosted models endpoint;
/// which one is used is chosen at runtime from the saved <c>Ai:Provider</c> setting, not at
/// registration time.
/// </summary>
/// <remarks>
/// Internal, like <c>AzureBlobMediaStorage</c> in <c>BlogIt.AzureStorage</c>: hosts resolve
/// <see cref="IAiService"/> from DI and never name this type. It was public while it lived in the
/// core package purely because it was registered from there.
/// </remarks>
internal sealed class OpenAiService(
    BlogItDbContext db,
    ISettingsService settings,
    BlogItOptions engineOptions) : IAiService
{
    // GitHub Copilot uses a fixed Azure OpenAI-compatible base URL.
    private const string GitHubCopilotBaseUrl = "https://models.inference.ai.azure.com";

    // Once a conversation's non-compacted message count reaches this, the oldest half is folded
    // into conversation.Summary (via one extra LLM call) and those rows are marked IsCompacted
    // instead of deleted — they stay visible in the admin's chat UI but are excluded from the LLM
    // request and from future compaction batches. Sending the summary as a system message going
    // forward is what keeps this conversation from ever hitting the provider's context-window
    // limit. Since only half is compacted each time, the non-compacted count sits at
    // HistoryCompactionThreshold/2 right after — it takes another HistoryCompactionThreshold/2 new
    // messages to trigger the next round.
    internal const int HistoryCompactionThreshold = 20;

    // Default models per provider.
    private const string DefaultChatModel = "gpt-4o-mini";
    private const string DefaultExportModel = "gpt-4o";
    private const string DefaultCopilotChatModel = "gpt-4o-mini";
    private const string DefaultCopilotExportModel = "gpt-4o";

    /// <summary>
    /// Builds a ChatClient for the configured provider.
    /// Supports "github-copilot" and "openai-compatible" (any OpenAI-compatible endpoint).
    /// </summary>
    private async Task<(ChatClient chat, ChatClient export)> BuildClientsAsync()
    {
        var provider = (await settings.GetAsync(SettingKeys.AiProvider) ?? "openai-compatible").Trim().ToLowerInvariant();
        var apiKey = await settings.GetAsync(SettingKeys.AiApiKey)
            ?? throw new InvalidOperationException("AI API key is not configured.");
        var chatModel = NullIfWhiteSpace(await settings.GetAsync(SettingKeys.AiModel));
        var exportModel = NullIfWhiteSpace(await settings.GetAsync(SettingKeys.AiExportModel));

        if (provider == "github-copilot")
        {
            // GitHub Copilot: fixed base URL, PAT as API key
            var options = new OpenAIClientOptions { Endpoint = new Uri(GitHubCopilotBaseUrl) };
            var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
            return (
                client.GetChatClient(chatModel ?? DefaultCopilotChatModel),
                client.GetChatClient(exportModel ?? DefaultCopilotExportModel)
            );
        }
        else
        {
            // OpenAI-compatible: use custom base URL if provided, otherwise api.openai.com
            var baseUrl = await settings.GetAsync(SettingKeys.AiBaseUrl);
            var endpoint = ResolveEndpoint(baseUrl, engineOptions.AllowPrivateAiEndpoints);
            var options = endpoint is null ? null : new OpenAIClientOptions { Endpoint = endpoint };

            var client = options is not null
                ? new OpenAIClient(new ApiKeyCredential(apiKey), options)
                : new OpenAIClient(new ApiKeyCredential(apiKey));

            return (
                client.GetChatClient(chatModel ?? DefaultChatModel),
                client.GetChatClient(exportModel ?? DefaultExportModel)
            );
        }
    }

    /// <summary>
    /// Turns the stored <c>Ai:BaseUrl</c> setting into the endpoint to call, or
    /// <see langword="null"/> to use the client's own default (<c>api.openai.com</c>).
    /// </summary>
    /// <remarks>
    /// Checked here and not only in <c>SiteSettingsValidator</c> on purpose. Validation guards what
    /// can be written from now on; this guards what is already in the database — a value saved before
    /// the check existed, or written by any other route into the settings table — and it is this line
    /// that actually hands the API key over, so this is where the decision has to hold. A private
    /// endpoint is refused rather than ignored: silently falling back to OpenAI would send the key to
    /// a different party than the operator configured.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The stored value is not an absolute http(s) URL, or points into private address space while
    /// <see cref="BlogItOptions.AllowPrivateAiEndpoints"/> is <see langword="false"/>.
    /// </exception>
    internal static Uri? ResolveEndpoint(string? baseUrl, bool allowPrivateAiEndpoints)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return null;

        var trimmed = baseUrl.Trim();
        if (!UrlValidator.IsValidAbsoluteHttpUrl(trimmed))
        {
            throw new InvalidOperationException(
                "The configured AI base URL is not an absolute http:// or https:// URL.");
        }

        if (!allowPrivateAiEndpoints && UrlValidator.IsPrivateOrLocalHttpUrl(trimmed))
        {
            throw new InvalidOperationException(
                "The configured AI base URL points at a loopback, link-local, or private address. "
                + "Set BlogItOptions.AllowPrivateAiEndpoints = true to allow a self-hosted model on "
                + "this machine or private network.");
        }

        return new Uri(trimmed);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<AiConversationDetailDto> SendMessageAsync(
        Guid conversationId,
        string userContent,
        CancellationToken cancellationToken = default)
    {
        var conversation = await db.AiConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");

        // Persist user message first
        var userMessage = new AiMessage
        {
            ConversationId = conversationId,
            Role = "user",
            Content = userContent
        };
        db.AiMessages.Add(userMessage);
        await db.SaveChangesAsync(cancellationToken);

        var (chatClient, _) = await BuildClientsAsync();

        IReadOnlyList<AiMessage> orderedMessages = SelectHistoryForRequest(conversation);

        var (toCompact, remaining) = SelectCompactionBatch(orderedMessages, HistoryCompactionThreshold);
        if (toCompact.Count > 0)
        {
            conversation.Summary = await SummarizeAsync(chatClient, conversation.Summary, toCompact, cancellationToken);
            foreach (var message in toCompact)
                message.IsCompacted = true;
            orderedMessages = remaining;
        }

        var allMessages = BuildRequestMessages(conversation.Summary, orderedMessages);

        var assistantContent = await CompleteChatTextAsync(chatClient, allMessages, cancellationToken);

        var assistantMessage = new AiMessage
        {
            ConversationId = conversationId,
            Role = "assistant",
            Content = assistantContent
        };
        db.AiMessages.Add(assistantMessage);
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        // Reload for accurate mapping
        await db.Entry(conversation).Collection(c => c.Messages).LoadAsync(cancellationToken);
        return MapConversation(conversation);
    }

    /// <summary>
    /// The non-compacted history, oldest first, that goes to the model for one chat turn.
    /// </summary>
    /// <remarks>
    /// Deliberately does <em>not</em> append the just-added user message: callers load
    /// <paramref name="conversation"/> tracked with <c>Include(c =&gt; c.Messages)</c>, so EF
    /// relationship fixup has already placed that message into the collection by the time this
    /// runs. Appending it as well sent every user turn to the provider twice — doubling the token
    /// bill — and left the list one long, firing <see cref="SelectCompactionBatch"/> a message
    /// early. Pinned by <c>AiRequestMessageTests</c>, which reproduces the fixup against a real
    /// <see cref="BlogItDbContext"/>.
    /// </remarks>
    internal static IReadOnlyList<AiMessage> SelectHistoryForRequest(AiConversation conversation) =>
        conversation.Messages
            .Where(m => !m.IsCompacted)
            .OrderBy(m => m.CreatedAt)
            .ToList();

    /// <summary>
    /// Assembles the exact outbound request: the running summary as a leading system message when
    /// there is one, then the ordered history mapped to user/assistant turns.
    /// </summary>
    internal static List<ChatMessage> BuildRequestMessages(
        string? summary,
        IReadOnlyList<AiMessage> orderedMessages)
    {
        var allMessages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            allMessages.Add(ChatMessage.CreateSystemMessage(
                $"Summary of earlier messages in this conversation (already removed from history):\n{summary}"));
        }
        allMessages.AddRange(orderedMessages.Select<AiMessage, ChatMessage>(m =>
            m.Role == "user"
                ? ChatMessage.CreateUserMessage(m.Content)
                : ChatMessage.CreateAssistantMessage(m.Content)));
        return allMessages;
    }

    /// <summary>
    /// Pure decision logic for <see cref="HistoryCompactionThreshold"/>: once
    /// <paramref name="orderedMessages"/> reaches <paramref name="threshold"/>, the oldest half
    /// is returned as the batch to compact, and the newer half as what remains. Below the
    /// threshold, nothing is compacted. Kept separate from the LLM call so the "when and how
    /// much" policy is unit-testable without a real chat provider.
    /// </summary>
    internal static (IReadOnlyList<AiMessage> ToCompact, IReadOnlyList<AiMessage> Remaining) SelectCompactionBatch(
        IReadOnlyList<AiMessage> orderedMessages,
        int threshold)
    {
        if (orderedMessages.Count < threshold)
            return ([], orderedMessages);

        var compactCount = orderedMessages.Count / 2;
        return (orderedMessages.Take(compactCount).ToList(), orderedMessages.Skip(compactCount).ToList());
    }

    private static async Task<string> SummarizeAsync(
        ChatClient chatClient,
        string? existingSummary,
        IReadOnlyList<AiMessage> messagesToCompact,
        CancellationToken cancellationToken)
    {
        var transcript = string.Join(
            "\n\n",
            messagesToCompact.Select(m => $"{m.Role}: {m.Content}"));

        var prompt = string.IsNullOrWhiteSpace(existingSummary)
            ? $"Summarize the following conversation excerpt concisely, preserving key facts, decisions, and any content the user explicitly wants kept (e.g. draft text, titles, structure). This summary will replace these raw messages as context for the rest of the conversation.\n\n{transcript}"
            : $"Here is the running summary of an earlier part of this conversation:\n{existingSummary}\n\nMerge it with the following additional messages into one updated, still-concise summary, preserving key facts, decisions, and any content the user explicitly wants kept:\n\n{transcript}";

        var summaryMessages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(
                "You compress conversation history into a concise summary for context continuity. Be factual and terse."),
            ChatMessage.CreateUserMessage(prompt)
        };

        return await CompleteChatTextAsync(chatClient, summaryMessages, cancellationToken);
    }

    public async Task<BlogPost> ExportToDraftAsync(
        Guid conversationId,
        Guid authorId,
        string? additionalInstructions,
        CancellationToken cancellationToken = default)
    {
        var conversation = await db.AiConversations
            .Include(c => c.Messages)
            .FirstOrDefaultAsync(c => c.Id == conversationId, cancellationToken)
            ?? throw new KeyNotFoundException("Conversation not found.");

        // Messages folded into conversation.Summary by history compaction stay in the DB (so the
        // admin's chat UI still shows them) but are excluded here to avoid feeding the LLM the
        // same content twice — once compacted, once raw.
        IEnumerable<string> history = conversation.Messages
            .Where(m => !m.IsCompacted)
            .OrderBy(m => m.CreatedAt)
            .Select(m => $"{m.Role}: {m.Content}");

        if (!string.IsNullOrWhiteSpace(conversation.Summary))
            history = [$"summary of earlier messages: {conversation.Summary}", .. history];

        var systemPrompt = """
            You are an expert blog writer. Based on the following brainstorm conversation,
            write a complete, well-structured blog post in Markdown format.

            Start with this exact metadata block:
            SEO Title: A compelling search title, no more than 60 characters
            SEO Description: A useful search description, no more than 160 characters
            SEO Keywords: five to eight comma-separated search phrases
            Tags: three to six concise comma-separated topic tags
            ---BEGIN ARTICLE---

            After the marker, use this exact article structure:
            1. A # heading with the article title on the first line.
            2. A blockquote (> text) immediately after the title with a 1-2 sentence summary.
            3. The full article body with proper ## subheadings, paragraphs, and lists.

            Write naturally and engagingly. Do not include meta-commentary about the task.
            """;

        if (!string.IsNullOrWhiteSpace(additionalInstructions))
            systemPrompt += $"\n\nAdditional instructions from the author: {additionalInstructions}";

        var messages = new List<ChatMessage>
        {
            ChatMessage.CreateSystemMessage(systemPrompt),
            ChatMessage.CreateUserMessage(
                "Brainstorm conversation:\n\n" + string.Join("\n\n", history))
        };

        var (_, exportClient) = await BuildClientsAsync();
        var generated = await CompleteChatTextAsync(exportClient, messages, cancellationToken);

        var lines = generated.Replace("\r\n", "\n").Split('\n');
        var articleMarker = Array.FindIndex(
            lines,
            line => line.Trim().Equals("---BEGIN ARTICLE---", StringComparison.OrdinalIgnoreCase));
        var titleLine = Array.FindIndex(lines, line => line.StartsWith("# "));
        var articleLines = articleMarker >= 0
            ? lines[(articleMarker + 1)..]
            : titleLine >= 0
                ? lines[titleLine..]
                : lines;

        var title = articleLines.FirstOrDefault(l => l.StartsWith("# "))?.TrimStart('#').Trim()
            ?? conversation.Title;

        var summary = articleLines.FirstOrDefault(l => l.StartsWith("> "))?.TrimStart('>', ' ').Trim()
            ?? string.Empty;

        var content = string.Join('\n', articleLines
            .SkipWhile(l => l.StartsWith("# ") || l.StartsWith("> ") || string.IsNullOrWhiteSpace(l)))
            .Trim();
        var tags = GetMetadataValue(lines, "Tags")
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(8)
            .ToList();
        var seoTitle = GetMetadataValue(lines, "SEO Title");
        var seoDescription = GetMetadataValue(lines, "SEO Description");
        var seoKeywords = GetMetadataValue(lines, "SEO Keywords");

        seoTitle = string.IsNullOrWhiteSpace(seoTitle) ? title : seoTitle;
        if (tags.Count == 0 && !string.IsNullOrWhiteSpace(seoKeywords))
        {
            tags = seoKeywords
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(6)
                .ToList();
        }
        if (tags.Count == 0)
            tags = GetFallbackTags(title);

        var descriptionSource = string.IsNullOrWhiteSpace(summary) ? content : summary;
        seoDescription = string.IsNullOrWhiteSpace(seoDescription)
            ? Truncate(MarkdownHelper.ToPlainText(descriptionSource), 160)
            : Truncate(seoDescription, 160);
        seoKeywords = string.IsNullOrWhiteSpace(seoKeywords)
            ? string.Join(", ", tags)
            : seoKeywords;

        var slug = await NextDraftSlugAsync(db, title, cancellationToken);

        var post = new BlogPost
        {
            Title = title,
            Slug = slug,
            Summary = summary,
            Content = content,
            SeoTitle = seoTitle,
            SeoDescription = seoDescription,
            SeoKeywords = seoKeywords,
            IsPublished = false,
            AuthorId = authorId
        };

        post.Tags = await TagResolver.ResolveAsync(db, tags, cancellationToken);
        db.BlogPosts.Add(post);
        conversation.LinkedDraftId = post.Id;
        conversation.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return post;
    }

    /// <summary>
    /// Picks the slug for a new draft from the title the model wrote.
    /// </summary>
    /// <remarks>
    /// A named seam rather than three lines inline, because the rest of
    /// <see cref="ExportToDraftAsync"/> needs a live provider to reach and this is the part with
    /// consequences: a title the model wrote is as likely to be Cyrillic or CJK as any other, and a
    /// draft that slugified to nothing became a post nothing could address.
    /// </remarks>
    internal static Task<string> NextDraftSlugAsync(
        BlogItDbContext db,
        string title,
        CancellationToken cancellationToken = default) =>
        SlugHelper.EnsureUniqueAsync(
            SlugHelper.SlugifyOrFallback(title),
            db.BlogPosts.Select(post => post.Slug),
            ContentLimits.SlugLength,
            cancellationToken);

    private static async Task<string> CompleteChatTextAsync(
        ChatClient client,
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var content = new StringBuilder();
        var updates = client.CompleteChatStreamingAsync(messages, cancellationToken: cancellationToken);

        await foreach (var update in updates.WithCancellation(cancellationToken))
        {
            foreach (var part in update.ContentUpdate)
                content.Append(part.Text);
        }

        if (content.Length == 0)
            throw new InvalidOperationException("The AI provider completed the request without returning any text.");

        return content.ToString();
    }

    private static string GetMetadataValue(IEnumerable<string> lines, string name)
    {
        var prefix = $"{name}:";
        var line = lines.FirstOrDefault(value =>
            value.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return line is null ? string.Empty : line[(line.IndexOf(':') + 1)..].Trim();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

    private static List<string> GetFallbackTags(string title)
    {
        string[] stopWords = ["about", "after", "from", "into", "that", "the", "this", "with", "your"];
        return title
            .Split([' ', '-', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(word => word.Length > 3 && !stopWords.Contains(word, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static AiConversationDetailDto MapConversation(AiConversation c) =>
        new(c.Id, c.Title, c.CreatedAt, c.UpdatedAt, c.LinkedDraftId,
            c.Messages
                .OrderBy(m => m.CreatedAt)
                .Select(m => new AiMessageDto(m.Id, m.Role, m.Content, m.CreatedAt))
                .ToList());
}
