namespace BlogIt.Shared.Entities;

public class BlogPost : IConcurrencyStamped, ISeoMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <inheritdoc cref="IConcurrencyStamped.ConcurrencyStamp"/>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;

    /// <summary>Markdown summary, always shown in listings.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>Full markdown content. Null means summary-only post — no full page rendered.</summary>
    public string? Content { get; set; }

    public bool IsPublished { get; set; }
    public bool HasBeenPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime? ScheduledPublishAt { get; set; }
    public DateTime? ScheduledUnpublishAt { get; set; }

    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? SeoKeywords { get; set; }
    public string? OgImageUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Guid AuthorId { get; set; }
    public AppUser? Author { get; set; }

    public ICollection<Tag> Tags { get; set; } = [];
}
