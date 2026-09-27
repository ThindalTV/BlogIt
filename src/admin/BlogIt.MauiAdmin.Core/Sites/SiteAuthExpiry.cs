namespace BlogIt.MauiAdmin.Core.Sites;

/// <summary>
/// The rules for reacting to a 401: which site it belongs to, and whether it still means that
/// site's session has ended.
/// </summary>
/// <remarks>
/// <para>
/// The site is read from the failed request itself rather than from the active profile. Asking
/// for the active profile after the response arrives is a race: switching sites while a request is
/// in flight used to attribute its 401 to the newly active site and sign that one out instead.
/// </para>
/// <para>
/// A 401 only ends the session whose token it was sent with. When a dashboard fires several
/// requests at once and they all come back 401, the first one clears the token and the rest find
/// it already gone, so the user is sent to the login screen once rather than once per request. A
/// 401 for a token the user has since replaced by signing in again is stale and ignored too.
/// </para>
/// </remarks>
public static class SiteAuthExpiry
{
    /// <summary>
    /// The profile whose API root contains <paramref name="requestUri"/>, preferring the most
    /// specific one when two sites share a host under different API paths.
    /// </summary>
    public static SiteProfile? ProfileForRequest(Uri? requestUri, IEnumerable<SiteProfile> profiles)
    {
        if (requestUri is not { IsAbsoluteUri: true })
            return null;

        return profiles
            .Where(profile => profile.ApiBaseUri.IsBaseOf(requestUri))
            .OrderByDescending(profile => profile.ApiBaseUri.AbsolutePath.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether a 401 for a request that carried <paramref name="sentToken"/> should clear the
    /// site's session, given the token stored for it now.
    /// </summary>
    /// <param name="sentToken">The bearer token on the failed request, or <see langword="null"/>
    /// when it was sent without one because the stored token had already expired locally.</param>
    /// <param name="storedToken">The token stored for the site at the time of the check.</param>
    public static bool ShouldExpire(string? sentToken, string? storedToken)
    {
        // Already cleared, by a concurrent 401 or by signing out.
        if (storedToken is null)
            return false;

        // Sent without a token: the stored one is expired, so the session has ended.
        if (sentToken is null)
            return true;

        // Sent with a token the user has since replaced by signing in again.
        return string.Equals(sentToken, storedToken, StringComparison.Ordinal);
    }
}
