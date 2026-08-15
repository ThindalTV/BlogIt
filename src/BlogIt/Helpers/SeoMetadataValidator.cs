namespace BlogIt.Shared.Helpers;

/// <summary>
/// Validates the SEO metadata fields against <see cref="SeoLimits"/> before they reach the
/// database.
/// </summary>
/// <remarks>
/// These columns used to be <c>nvarchar(max)</c>, so anything was accepted and stored off-row.
/// Giving them a width fixes the storage cost but turns an over-long value into a database error —
/// an unhandled <c>DbUpdateException</c> surfacing as a 500. This is the other half of that change:
/// the same limits enforced at the boundary, returning a 400 that names the field.
/// </remarks>
public static class SeoMetadataValidator
{
    public static Dictionary<string, string[]> Validate(
        string? seoTitle,
        string? seoDescription,
        string? seoKeywords,
        string? ogImageUrl)
    {
        var errors = new Dictionary<string, string[]>();

        Check(errors, "seoTitle", "SEO title", seoTitle, SeoLimits.TitleLength);
        Check(errors, "seoDescription", "SEO description", seoDescription, SeoLimits.DescriptionLength);
        Check(errors, "seoKeywords", "SEO keywords", seoKeywords, SeoLimits.KeywordsLength);
        Check(errors, "ogImageUrl", "Open Graph image URL", ogImageUrl, SeoLimits.OgImageUrlLength);

        return errors;
    }

    private static void Check(
        Dictionary<string, string[]> errors,
        string field,
        string label,
        string? value,
        int maxLength)
    {
        if (value is not null && value.Length > maxLength)
            errors[field] = [$"{label} must be {maxLength} characters or fewer."];
    }
}
