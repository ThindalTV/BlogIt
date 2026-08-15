namespace BlogIt.Shared;

/// <summary>
/// Maximum lengths for the SEO metadata fields on posts and pages.
/// </summary>
/// <remarks>
/// One source of truth, shared by three places that must agree: the EF column widths, the
/// server-side validation that returns a 400, and any client that wants to validate before
/// sending. Bounding the columns without bounding the input would only move the failure from a
/// silent off-row write to an unhandled database error.
/// <para>
/// The values are generous rather than best-practice — a search title works best under 60
/// characters and a description under 160 — because their job is to keep the columns off
/// <c>nvarchar(max)</c>, not to enforce editorial guidance.
/// </para>
/// </remarks>
public static class SeoLimits
{
    public const int TitleLength = 300;
    public const int DescriptionLength = 1000;
    public const int KeywordsLength = 1000;
    public const int OgImageUrlLength = 1000;
}
