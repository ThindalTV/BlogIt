namespace BlogIt.Shared.Entities;

public class AiConversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }

    /// <summary>Set after this conversation is exported to a draft blog post.</summary>
    public Guid? LinkedDraftId { get; set; }
    public BlogPost? LinkedDraft { get; set; }

    /// <summary>
    /// Rolling LLM-generated summary of older messages that have been compacted out of
    /// <see cref="Messages"/> (see <c>AiService.HistoryCompactionThreshold</c>). Sent as a
    /// system message ahead of the remaining raw messages on every subsequent turn; null until
    /// the conversation is long enough to trigger the first compaction.
    /// </summary>
    public string? Summary { get; set; }

    public ICollection<AiMessage> Messages { get; set; } = [];
}
