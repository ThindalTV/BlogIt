namespace BlogIt.Shared.Entities;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BlogPost> BlogPosts { get; set; } = [];
    public ICollection<AiConversation> AiConversations { get; set; } = [];
    public ICollection<MediaFile> MediaFiles { get; set; } = [];
}
