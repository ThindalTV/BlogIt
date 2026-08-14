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
