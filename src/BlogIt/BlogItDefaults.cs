namespace BlogIt;

public static class BlogItDefaults
{
    public const string AdminPath = "/blogit";
    public const string ApiPath = "/api";
    public const string MediaPath = "/media";

    public const string AuthenticationScheme = "BlogIt.Jwt";
    public const string AdminAuthorizationPolicy = "BlogIt.Admin";
    public const string LoginRateLimiterPolicy = "BlogIt.LoginRateLimit";
}

/// <summary>
/// Endpoint names for the routes BlogIt maps at the site root, for use with
/// <c>LinkGenerator</c> or <c>IEndpointNameMetadata</c>.
/// </summary>
/// <remarks>
/// Every name is prefixed. Endpoint names are a single flat namespace shared with the host, and
/// ASP.NET Core throws at startup on a duplicate — an unprefixed <c>"RssFeed"</c> from a library
/// would break any host that had already used that name for its own endpoint.
/// </remarks>
public static class BlogItEndpointNames
{
    /// <summary>Name of the <c>/rss.xml</c> endpoint.</summary>
    public const string RssFeed = "BlogIt.RssFeed";

    /// <summary>Name of the <c>/atom.xml</c> endpoint.</summary>
    public const string AtomFeed = "BlogIt.AtomFeed";

    /// <summary>Name of the <c>/sitemap.xml</c> endpoint.</summary>
    public const string Sitemap = "BlogIt.Sitemap";

    /// <summary>Name of the <c>/robots.txt</c> endpoint.</summary>
    public const string RobotsTxt = "BlogIt.RobotsTxt";
}

/// <summary>Claim types BlogIt mints into its own tokens beyond the registered JWT set.</summary>
public static class BlogItClaimTypes
{
    /// <summary>
    /// The value of <c>AppUser.SecurityStamp</c> at the moment the token was issued. Checked
    /// against the stored row on every authenticated request; a mismatch means the account's
    /// sessions were deliberately invalidated after this token was minted.
    /// </summary>
    public const string SecurityStamp = "sstamp";
}
