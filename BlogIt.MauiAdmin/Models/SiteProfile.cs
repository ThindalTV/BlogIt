namespace BlogIt.MauiAdmin.Models;

/// <summary>Represents a saved site connection (name, API URL, and auth token).</summary>
public class SiteProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public string? JwtToken { get; set; }
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
    public DateTime? TokenExpiresAt { get; set; }

    public bool IsTokenValid =>
        JwtToken is not null &&
        TokenExpiresAt.HasValue &&
        TokenExpiresAt.Value > DateTime.UtcNow.AddMinutes(1);
}
