namespace BlogIt.Shared.Helpers;

/// <summary>
/// The two halves of BlogIt's convention for optional text: <see langword="null"/> means the value
/// does not exist, an empty string means it exists and is empty.
/// </summary>
/// <remarks>
/// <para>
/// Clients normalise on the way in with <see cref="OrNull"/>: a field the author never filled in
/// does not exist, so it is stored as <see langword="null"/> rather than <c>""</c>. A caller that
/// genuinely means "this value is present and empty" passes the empty string through untouched, and
/// the API stores it verbatim.
/// </para>
/// <para>
/// Readers fall back with <see cref="FirstPresent"/> rather than <c>??</c>. Null-coalescing only
/// skips <see langword="null"/>, so <c>post.SeoTitle ?? post.Title</c> returns <c>""</c> for any row
/// that stored a blank SEO title — which is how every post saved without SEO fields came to render
/// an empty <c>&lt;title&gt;</c> and an empty <c>og:title</c>. Existing rows still hold those empty
/// strings, so reading has to tolerate them whatever the writers do from now on.
/// </para>
/// </remarks>
public static class OptionalText
{
    /// <summary>
    /// Returns <paramref name="value"/>, or <see langword="null"/> if it is absent or only
    /// whitespace — for normalising "the author left this blank" into "does not exist".
    /// </summary>
    public static string? OrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// Returns the first candidate that actually carries text, treating <see langword="null"/>,
    /// <c>""</c> and whitespace alike as absent. Returns <see langword="null"/> if none do.
    /// </summary>
    public static string? FirstPresent(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        }

        return null;
    }
}
