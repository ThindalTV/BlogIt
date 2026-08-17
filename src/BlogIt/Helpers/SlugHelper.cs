using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BlogIt.Shared.Helpers;

public static partial class SlugHelper
{
    /// <summary>
    /// Characters reserved for the collision counter <see cref="EnsureUnique"/> appends: the hyphen
    /// plus up to six digits.
    /// </summary>
    /// <remarks>
    /// Held back from the base slug up front, so every candidate this class can produce shares one
    /// prefix and <see cref="CollisionKeys"/> can express the whole candidate set as a single
    /// index-seekable lookup. Sizing it for six digits means a base slug that fills the column has
    /// to collide a million times before the reservation is exhausted.
    /// </remarks>
    private const int CounterReservation = 7;

    /// <summary>Hex characters kept from the fallback hash. See <see cref="SlugifyOrFallback"/>.</summary>
    private const int FallbackTokenLength = 8;

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex NonSlugChar();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex MultipleHyphens();

    /// <summary>Converts any string into a URL-safe slug, which may be empty.</summary>
    /// <remarks>
    /// Returns an empty string for input holding nothing in <c>[a-z0-9-]</c> — a Cyrillic or CJK
    /// title, or one made only of punctuation. That is deliberate and depended on: the post and page
    /// update paths compare this against the stored slug to decide whether a rename was asked for,
    /// and an empty result there means "no slug was requested", not "slugify harder". Callers
    /// creating content — posts, pages, tags — want <see cref="SlugifyOrFallback"/> instead, since
    /// for them an empty slug means content that cannot be addressed.
    /// </remarks>
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
    /// Converts any string into a URL-safe slug that is never empty, falling back to a short token
    /// derived from the input when nothing in it survives slugification.
    /// </summary>
    /// <remarks>
    /// The guard <c>PagesApi</c> used to carry alone, generalised. A blank slug is not a cosmetic
    /// problem: a post or page reached only by its slug becomes permanently unreachable, and since a
    /// slug locks on first publication there is nothing the admin UI can do about it afterwards.
    /// <para>
    /// The fallback is the first <see cref="FallbackTokenLength"/> hex characters of the SHA-256 of
    /// the trimmed input. Three options were weighed:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// Transliteration reads best — <c>privet-mir</c> beats <c>830d1964</c> — but it needs a table
    /// per script, there are several competing romanisations of Cyrillic to pick a losing side of,
    /// and Chinese needs a multi-thousand-entry pinyin dictionary. That is a data dependency this
    /// package does not have and cannot be right about for every language.
    /// </item>
    /// <item>
    /// Keeping the original characters (a valid IRI, <c>/blog/привет-мир</c>) reads best of all and
    /// browsers handle it, but it changes what <see cref="Slugify"/> means for every existing caller
    /// including tag slugs, and every place that writes a slug into a feed or sitemap would need to
    /// escape it. Too wide a change to make while fixing this.
    /// </item>
    /// <item>
    /// A content-derived token is ugly and universal, needs no data, and — the deciding property —
    /// is <em>stable</em>. The same title yields the same slug in every process and on every machine,
    /// so a draft cannot be previewed at one address and published at another. A random or
    /// time-based suffix fails that outright, and an id-derived one only holds if the id already
    /// exists when the slug is computed, which is not true of every caller here.
    /// </item>
    /// </list>
    /// <para>
    /// SHA-256 rather than <see cref="string.GetHashCode()"/> precisely because the latter is
    /// randomised per process, which would move a slug across an app restart.
    /// </para>
    /// </remarks>
    public static string SlugifyOrFallback(string? input)
    {
        var trimmed = input?.Trim() ?? string.Empty;
        var slug = Slugify(trimmed);
        if (slug.Length > 0)
            return slug;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(trimmed));
        return Convert.ToHexStringLower(hash)[..FallbackTokenLength];
    }

    /// <summary>
    /// The two values that bound every slug <see cref="EnsureUnique"/> could return for
    /// <paramref name="baseSlug"/>: the unsuffixed candidate, and the prefix every suffixed one
    /// starts with.
    /// </summary>
    /// <remarks>
    /// Exists so <see cref="EnsureUniqueAsync"/> can ask the database for the handful of rows that
    /// could actually collide instead of materialising every slug in the table on each create, which
    /// is what the create paths used to do. Both keys are needed rather than one: when
    /// <paramref name="baseSlug"/> is long enough to be truncated, the unsuffixed candidate keeps
    /// the full width while the suffixed ones are cut back to make room for the counter, so the two
    /// do not share a prefix.
    /// <para>
    /// Slugs only ever contain <c>[a-z0-9-]</c>, so the prefix can never carry a <c>LIKE</c>
    /// wildcard into the translated query.
    /// </para>
    /// </remarks>
    internal static (string Exact, string Prefix) CollisionKeys(string baseSlug, int maxLength) =>
        (Truncate(baseSlug, maxLength), Truncate(baseSlug, maxLength - CounterReservation) + "-");

    /// <summary>
    /// Ensures slug uniqueness by appending -2, -3, etc. when duplicates exist, keeping the result
    /// within <paramref name="maxLength"/>.
    /// </summary>
    /// <param name="maxLength">
    /// The width of the column the slug is destined for. Required rather than optional on purpose:
    /// a caller that forgets it is the defect this parameter exists to close, since a title filling
    /// the title column produces a slug filling the slug column and the counter then has nowhere to
    /// go.
    /// </param>
    public static string EnsureUnique(string baseSlug, IEnumerable<string> existingSlugs, int maxLength)
    {
        var existing = new HashSet<string>(existingSlugs, StringComparer.OrdinalIgnoreCase);
        var (exact, prefix) = CollisionKeys(baseSlug, maxLength);
        if (!existing.Contains(exact))
            return exact;

        // One stem for every suffixed candidate, cut back by the full CounterReservation rather than
        // by the length of the suffix actually being appended. Shortening per candidate would fit the
        // column just as well but would give "-2" and "-10" different stems, and then no single
        // prefix would describe the candidate set for CollisionKeys to query on.
        //
        // That fixes the counter at six digits: a base slug filling the column would have to collide
        // 999,999 times to outgrow the reservation, which needs that many rows sharing one 493-char
        // stem. Past that the value would exceed the column and the duplicate-key handling added with
        // it would answer 409 rather than 500 — a bound worth stating, not worth branching on.
        var counter = 2;
        string candidate;
        do
        {
            candidate = $"{prefix}{counter++}";
        } while (existing.Contains(candidate));

        return candidate;
    }

    /// <summary>
    /// Ensures slug uniqueness against a database column, reading only the rows that could collide.
    /// </summary>
    /// <param name="existingSlugs">
    /// The unfiltered slug column, e.g. <c>db.BlogPosts.Select(p =&gt; p.Slug)</c>. Narrowed here
    /// rather than by the caller so no caller can forget to.
    /// </param>
    /// <remarks>
    /// The comparison is ordinal-insensitive in memory but whatever the database collation says on
    /// the server side. That difference is unreachable in practice: every slug is produced by
    /// <see cref="Slugify"/>, which lowercases, so no two stored slugs can differ by case alone.
    /// </remarks>
    public static async Task<string> EnsureUniqueAsync(
        string baseSlug,
        IQueryable<string> existingSlugs,
        int maxLength,
        CancellationToken cancellationToken = default)
    {
        var (exact, prefix) = CollisionKeys(baseSlug, maxLength);
        var candidates = await existingSlugs
            .Where(slug => slug == exact || slug.StartsWith(prefix))
            .ToListAsync(cancellationToken);

        return EnsureUnique(baseSlug, candidates, maxLength);
    }

    private static string Truncate(string value, int maxLength) =>
        maxLength <= 0 ? string.Empty
        : value.Length <= maxLength ? value
        : value[..maxLength];
}
