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

    public ICollection<AiMessage> Messages { get; set; } = [];
}
