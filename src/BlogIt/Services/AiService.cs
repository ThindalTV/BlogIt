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

namespace BlogIt.Services;

public class AiService(BlogItDbContext db, ISettingsService settings) : IAiService
{
    // GitHub Copilot uses a fixed Azure OpenAI-compatible base URL.
    private const string GitHubCopilotBaseUrl = "https://models.inference.ai.azure.com";

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
            OpenAIClientOptions? options = null;
            if (!string.IsNullOrWhiteSpace(baseUrl))
                options = new OpenAIClientOptions { Endpoint = new Uri(baseUrl) };

            var client = options is not null
                ? new OpenAIClient(new ApiKeyCredential(apiKey), options)
                : new OpenAIClient(new ApiKeyCredential(apiKey));

            return (
                client.GetChatClient(chatModel ?? DefaultChatModel),
                client.GetChatClient(exportModel ?? DefaultExportModel)
            );
        }
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

        // Build full chat history (including the new user message)
        var allMessages = conversation.Messages
            .Append(userMessage)
            .OrderBy(m => m.CreatedAt)
            .Select<AiMessage, ChatMessage>(m =>
                m.Role == "user"
                    ? ChatMessage.CreateUserMessage(m.Content)
                    : ChatMessage.CreateAssistantMessage(m.Content))
            .ToList();

        var (chatClient, _) = await BuildClientsAsync();
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

        var history = conversation.Messages
            .OrderBy(m => m.CreatedAt)
            .Select(m => $"{m.Role}: {m.Content}");

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

        var slug = SlugHelper.Slugify(title);
        var existingSlugs = await db.BlogPosts.Select(p => p.Slug).ToListAsync(cancellationToken);
        slug = SlugHelper.EnsureUnique(slug, existingSlugs);

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
