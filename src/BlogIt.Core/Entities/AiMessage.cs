namespace BlogIt.Shared.Entities;

public class AiMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConversationId { get; set; }
    public AiConversation? Conversation { get; set; }

    /// <summary>"user" or "assistant"</summary>
    public string Role { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// True once this message has been folded into the conversation's rolling Summary by
    /// history compaction. The row is kept (so the admin's chat UI still shows it) but is
    /// excluded from the messages sent to the LLM and from future compaction batches.
    /// </summary>
    public bool IsCompacted { get; set; }
}
