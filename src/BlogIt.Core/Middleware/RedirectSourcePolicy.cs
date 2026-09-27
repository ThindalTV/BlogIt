namespace BlogIt.Middleware;

/// <summary>
/// The single answer to "may the blog's redirect table claim this URL?", shared by the write path
/// (<c>RedirectPathValidator</c>, which turns a no into a 400) and the read path
/// (<see cref="UrlRedirectMiddleware"/>, which turns a no into "not a redirect").
/// </summary>
/// <remarks>
/// Both paths check, not just the write path: rows outlive the configuration that allowed them. A
/// deployment that has been running with redirects on host URLs and then sets
/// <see cref="BlogItOptions.RedirectSourcePrefixes"/> is asking for those rows to stop being
/// honoured, and a write-time-only check would leave every one of them live — including a permanent
/// redirect on the host's login page, which is the case the option exists for.
/// </remarks>
internal static class RedirectSourcePolicy
{
    /// <summary>
    /// True when <paramref name="sourcePath"/> is inside one of the configured prefixes, or when no
    /// prefix is configured at all (the documented default: no prefix restriction).
    /// </summary>
    /// <param name="sourcePath">A normalized, absolute local path such as <c>/blog/old-post</c>.</param>
    /// <param name="options">The frozen engine options carrying the host's decision.</param>
    public static bool IsWithinConfiguredPrefixes(string sourcePath, BlogItOptions options)
    {
        var prefixes = options.RedirectSourcePrefixes;
        if (prefixes.Count == 0)
            return true;

        // The prefix itself counts as inside, but a longer path only when the next character is a
        // separator: otherwise a prefix of "/blog" would also hand the author "/blogger/...", which
        // is a different route and quite possibly the host's.
        return prefixes.Any(prefix =>
            sourcePath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || sourcePath.StartsWith(
                prefix.EndsWith('/') ? prefix : $"{prefix}/",
                StringComparison.OrdinalIgnoreCase));
    }
}
