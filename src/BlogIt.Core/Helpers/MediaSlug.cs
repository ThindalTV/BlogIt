namespace BlogIt.Helpers;

/// <summary>
/// Normalises and validates the optional, human-chosen URL path of a media file
/// (<c>docs/cv.pdf</c> → <c>/media/docs/cv.pdf</c>).
/// </summary>
/// <remarks>
/// Lower-cased on the way in so that <c>CV.pdf</c> and <c>cv.pdf</c> are the same URL on every
/// database: uniqueness and lookup would otherwise depend on the column's collation, which differs
/// between SQL Server and SQLite. Segments are restricted to characters that need no URL escaping,
/// and <c>.</c>/<c>..</c> are refused, so a slug can never be read as a traversal.
/// </remarks>
public static class MediaSlug
{
    public const int MaxLength = 200;

    /// <summary>
    /// Returns the normalised slug, or null for a blank value (meaning "no slug"). Check
    /// <see cref="Validate"/> on the result.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var trimmed = value?.Trim().Trim('/');
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.ToLowerInvariant();
    }

    /// <summary>Returns an error message for an invalid normalised slug, or null if it is fine.</summary>
    public static string? Validate(string? slug)
    {
        if (slug is null)
            return null;

        if (slug.Length > MaxLength)
            return $"Path must be {MaxLength} characters or fewer.";

        foreach (var segment in slug.Split('/'))
        {
            if (segment.Length == 0)
                return "Path cannot contain empty segments (\"//\").";
            if (segment is "." or "..")
                return "Path cannot contain \".\" or \"..\" segments.";
            foreach (var c in segment)
            {
                if (!(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '-' or '_' or '.'))
                    return "Path may only contain letters, digits, \"-\", \"_\", \".\" and \"/\".";
            }
        }

        return null;
    }
}
