namespace BlogIt.Shared.Entities;

public class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;

    /// <summary>URL slug — becomes the absolute path e.g. /about</summary>
    public string Slug { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
    public bool HasBeenPublished { get; set; }
    public DateTime? ScheduledPublishAt { get; set; }
    public DateTime? ScheduledUnpublishAt { get; set; }

    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? SeoKeywords { get; set; }
    public string? OgImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
