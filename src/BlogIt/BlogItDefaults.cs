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
