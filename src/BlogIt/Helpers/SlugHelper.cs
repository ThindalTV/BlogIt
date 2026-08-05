using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BlogIt.Shared.Helpers;

public static partial class SlugHelper
{
    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex NonSlugChar();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultipleHyphens();

    /// <summary>Converts any string into a URL-safe slug.</summary>
    public static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var normalized = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(char.ToLowerInvariant(c));
        }

        var slug = builder.ToString()
            .Replace(' ', '-')
            .Replace('_', '-');

        slug = NonSlugChar().Replace(slug, string.Empty);
        slug = MultipleHyphens().Replace(slug, "-");
        slug = slug.Trim('-');

        return slug;
    }

    /// <summary>
    /// Ensures slug uniqueness by appending -2, -3, etc. when duplicates exist.
    /// </summary>
    public static string EnsureUnique(string baseSlug, IEnumerable<string> existingSlugs)
    {
        var existing = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(baseSlug))
            return baseSlug;

        var counter = 2;
        string candidate;
        do
        {
            candidate = $"{baseSlug}-{counter++}";
        } while (existing.Contains(candidate));

        return candidate;
    }
}
