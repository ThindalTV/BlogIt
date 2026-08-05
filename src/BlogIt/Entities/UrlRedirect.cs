namespace BlogIt.Shared.Entities;

public class UrlRedirect
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SourcePath { get; set; } = string.Empty;
    public string TargetUrl { get; set; } = string.Empty;
    public bool IsPermanent { get; set; } = true;
    public bool IsAutomatic { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
