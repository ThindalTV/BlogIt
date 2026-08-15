namespace BlogIt.Shared.Entities;

/// <summary>
/// The SEO metadata fields carried identically by <see cref="BlogPost"/> and <see cref="Page"/>.
/// </summary>
/// <remarks>
/// Exists so the column widths and the validation that enforces them are declared once for both
/// entities rather than copied. The post and page code paths have already drifted apart once where
/// they were duplicated by hand.
/// </remarks>
public interface ISeoMetadata
{
    string? SeoTitle { get; set; }
    string? SeoDescription { get; set; }
    string? SeoKeywords { get; set; }
    string? OgImageUrl { get; set; }
}
