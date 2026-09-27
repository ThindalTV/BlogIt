namespace BlogIt.Shared.Entities;

public class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Changes whenever this account's existing sessions must stop working — today that means a
    /// password change. Every issued JWT carries the stamp it was minted with, and authentication
    /// rejects any token whose stamp no longer matches the row, which is what makes a password
    /// change take effect immediately instead of at the token's natural expiry.
    /// </summary>
    /// <remarks>
    /// A GUID is the right primitive here even though a signing secret would not be: this value
    /// is compared, never used as a credential. Knowing it lets an attacker do nothing — they
    /// would still have to sign a token — so it needs to be distinct on every change, not
    /// unguessable.
    /// </remarks>
    public string SecurityStamp { get; set; } = Guid.NewGuid().ToString("N");

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<BlogPost> BlogPosts { get; set; } = [];
    public ICollection<AiConversation> AiConversations { get; set; } = [];
    public ICollection<MediaFile> MediaFiles { get; set; } = [];
}
