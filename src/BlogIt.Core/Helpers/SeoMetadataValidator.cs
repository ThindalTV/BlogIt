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
        // Returns the dictionary rather than an IResult so a caller can add its own field errors —
        // the required ones in TextFieldValidator — and answer with a single 400 listing all of them.
        var errors = new Dictionary<string, string[]>();

        TextFieldValidator.CheckLength(
            errors, "seoTitle", "SEO title", seoTitle, SeoLimits.TitleLength);
        TextFieldValidator.CheckLength(
            errors, "seoDescription", "SEO description", seoDescription, SeoLimits.DescriptionLength);
        TextFieldValidator.CheckLength(
            errors, "seoKeywords", "SEO keywords", seoKeywords, SeoLimits.KeywordsLength);
        TextFieldValidator.CheckLength(
            errors, "ogImageUrl", "Open Graph image URL", ogImageUrl, SeoLimits.OgImageUrlLength);

        return errors;
    }
}
