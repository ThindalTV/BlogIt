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
}
