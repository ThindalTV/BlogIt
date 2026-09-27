using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogIt;

/// <summary>
/// The limits and partition keys behind BlogIt's rate limiter policies, kept out of
/// <see cref="BlogItServiceCollectionExtensions.AddBlogIt"/> so they can be asserted directly:
/// <c>RateLimiterOptions</c> keeps its policy map internal to ASP.NET Core, so a limit configured
/// inline there is only observable by sending enough requests to trip it.
/// </summary>
/// <remarks>
/// <para>
/// Every policy is a fixed window with no queue. A queue would convert a flood into latency for
/// every other caller in the same partition instead of a <c>429</c> for the flooder, which is the
/// opposite of what these routes need.
/// </para>
/// <para>
/// The options are returned as fresh instances rather than shared statics because the middleware
/// holds onto whatever it is handed for the lifetime of a partition; a shared mutable options object
/// would let one caller's edit reach every partition. The allocation happens once per new partition,
/// not per request.
/// </para>
/// </remarks>
internal static class BlogItRateLimiterPolicies
{
    /// <summary>
    /// 10 attempts per 5 minutes. Password guessing is the threat and each attempt costs a BCrypt
    /// verify; 2 a minute is hopeless for an attacker and far more than a person mistyping.
    /// </summary>
    internal static FixedWindowRateLimiterOptions Login => Window(10, TimeSpan.FromMinutes(5));

    /// <summary>
    /// 20 operations per 5 minutes, shared by change-password and user creation.
    /// </summary>
    /// <remarks>
    /// Change-password requires the current password, so this is a brute-force surface for a caller
    /// who already holds a token: 4 guesses a minute against a password meeting
    /// <c>PasswordPolicy</c> is not a threat. The ceiling is set at 20 rather than login's 10
    /// because user creation shares the bucket and an admin onboarding a small team does legitimately
    /// make a dozen calls in one sitting. Sharing one bucket is deliberate — both are rare,
    /// credential-touching administrative actions, and a caller doing 20 of them in five minutes is
    /// not doing normal admin work.
    /// </remarks>
    internal static FixedWindowRateLimiterOptions Account => Window(20, TimeSpan.FromMinutes(5));

    /// <summary>
    /// 60 requests per minute. <c>GET /setup/status</c> is anonymous and is used as a liveness probe
    /// — the Blazor admin calls it on load and <c>BlogIt.MauiAdmin</c>'s site probe calls it to
    /// validate a URL the user typed — so it needs headroom, but it also costs a database query on
    /// an unauthenticated route. One a second per caller is far above any client's need.
    /// <c>POST /setup/initialize</c> shares the bucket: it can succeed exactly once in a site's
    /// lifetime, and every later call is a cheap conflict.
    /// </summary>
    internal static FixedWindowRateLimiterOptions Setup => Window(60, TimeSpan.FromMinutes(1));

    /// <summary>
    /// 600 requests per minute — the one number here sized for ordinary visitors rather than for
    /// administrators.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every hit costs one indexed lookup on <c>MediaFile.BackendUrl</c> plus one storage open, and
    /// the route is hit once per image per page view by anonymous traffic. The arithmetic: an
    /// image-heavy gallery post of ~60 images is ~60 requests in a single burst, so 600 a minute lets
    /// one address load ten such pages a minute — roughly ten times a fast human, and much more than
    /// that in practice because responses carry <c>max-age=31536000</c>, so a returning visitor
    /// re-requests nothing.
    /// </para>
    /// <para>
    /// The residual risk is a shared egress address (carrier-grade NAT, a corporate proxy): 600 a
    /// minute is 10 a second for everyone behind it, ample for a blog but not for a very large shared
    /// network. That is the accepted trade, and it is why the figure is 600 and not the 60 an admin
    /// endpoint gets — a limit sized for an admin route would throttle a real gallery, which would be
    /// worse than not limiting this route at all.
    /// </para>
    /// </remarks>
    internal static FixedWindowRateLimiterOptions Media => Window(600, TimeSpan.FromMinutes(1));

    /// <summary>
    /// 30 requests per minute for the crawler-facing root documents.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>/sitemap.xml</c> is the expensive one: it loads every published post and page with no cap,
    /// where the feeds stop at <see cref="Services.FeedService.MaxItems"/>. A well-behaved crawler
    /// fetches these a handful of times a day, so 30 a minute per address is an enormous margin while
    /// still stopping one client from replaying a full-table scan thousands of times a minute.
    /// </para>
    /// <para>
    /// Throttling rather than caching, and rather than capping the sitemap's entry count. A cap would
    /// make <c>ISiteMetadataService.GetSitemapEntriesAsync</c> stop being "every URL BlogIt would
    /// list", which is its documented contract. A server-side cache was the closer call and was
    /// rejected on two counts: the endpoint and the data API would then be able to disagree — one
    /// serving a stale snapshot while the other reads live rows, which is precisely what the
    /// byte-identity test between them exists to prevent — and a crawler document that keeps
    /// advertising a stale URL set for minutes after a publish is a worse default than a fast query.
    /// </para>
    /// </remarks>
    internal static FixedWindowRateLimiterOptions RootDocument => Window(30, TimeSpan.FromMinutes(1));

    /// <summary>
    /// Partitions on the caller's address, falling back to one shared bucket when it is unavailable
    /// (behind some proxies, and in <c>TestServer</c>). Per-partition rather than global so one
    /// attacker cannot lock every other visitor out.
    /// </summary>
    internal static string ClientPartitionKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Partitions on the presented bearer token when there is one, falling back to
    /// <see cref="ClientPartitionKey"/>.
    /// </summary>
    /// <remarks>
    /// The threat the account policy answers is a stolen token guessing the account's current
    /// password. Partitioning on the address alone would let the holder reset the counter simply by
    /// moving; the token is the one thing they cannot change. The token's identity is used, never the
    /// token: partition keys reach diagnostics, and a bearer token is a credential, so what goes into
    /// the key is a SHA-256 of it.
    /// <para>
    /// The authenticated user id would be a better partition still, but it is not available here:
    /// the rate limiter middleware runs ahead of authorization, and authorization is what populates
    /// <c>HttpContext.User</c> for BlogIt's named scheme (there is no default authenticate scheme).
    /// Moving the limiter behind authorization to get the id would mean an anonymous flood reached
    /// the authentication machinery before being counted.
    /// </para>
    /// </remarks>
    internal static string SessionPartitionKey(HttpContext httpContext)
    {
        var header = httpContext.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = header[prefix.Length..].Trim();
            if (token.Length > 0)
            {
                return "token:" + Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(token)));
            }
        }

        return ClientPartitionKey(httpContext);
    }

    /// <summary>Every policy name BlogIt registers, in no particular order.</summary>
    internal static IReadOnlyList<string> Names { get; } =
    [
        BlogItDefaults.LoginRateLimiterPolicy,
        BlogItDefaults.AccountRateLimiterPolicy,
        BlogItDefaults.SetupRateLimiterPolicy,
        BlogItDefaults.MediaRateLimiterPolicy,
        BlogItDefaults.RootDocumentRateLimiterPolicy
    ];

    /// <summary>The limits <paramref name="policyName"/> is configured with.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The name is not one of <see cref="Names"/>.</exception>
    internal static FixedWindowRateLimiterOptions Limits(string policyName) => policyName switch
    {
        BlogItDefaults.LoginRateLimiterPolicy => Login,
        BlogItDefaults.AccountRateLimiterPolicy => Account,
        BlogItDefaults.SetupRateLimiterPolicy => Setup,
        BlogItDefaults.MediaRateLimiterPolicy => Media,
        BlogItDefaults.RootDocumentRateLimiterPolicy => RootDocument,
        _ => throw new ArgumentOutOfRangeException(
            nameof(policyName), policyName, "Not a BlogIt rate limiter policy.")
    };

    /// <summary>
    /// The bucket <paramref name="httpContext"/> counts against under
    /// <paramref name="policyName"/>. Only the account policy partitions on the session; everything
    /// else partitions on the caller's address.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The name is not one of <see cref="Names"/>.</exception>
    internal static string PartitionKey(string policyName, HttpContext httpContext) => policyName switch
    {
        BlogItDefaults.AccountRateLimiterPolicy => SessionPartitionKey(httpContext),
        BlogItDefaults.LoginRateLimiterPolicy
            or BlogItDefaults.SetupRateLimiterPolicy
            or BlogItDefaults.MediaRateLimiterPolicy
            or BlogItDefaults.RootDocumentRateLimiterPolicy => ClientPartitionKey(httpContext),
        _ => throw new ArgumentOutOfRangeException(
            nameof(policyName), policyName, "Not a BlogIt rate limiter policy.")
    };

    /// <summary>Registers every policy in <see cref="Names"/> on <paramref name="options"/>.</summary>
    internal static void AddTo(RateLimiterOptions options)
    {
        foreach (var policyName in Names)
        {
            options.AddPolicy(policyName, Create(policyName));
        }
    }

    /// <summary>The policy object registered for <paramref name="policyName"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The name is not one of <see cref="Names"/>.</exception>
    internal static IRateLimiterPolicy<string> Create(string policyName)
    {
        // Validate here rather than on first request: an unknown name would otherwise surface as a
        // throwing partition resolver in the middleware.
        _ = Limits(policyName);
        return new Policy(policyName);
    }

    /// <summary>
    /// One BlogIt policy, carrying its own rejection handler.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rejection handler is per-policy rather than <c>RateLimiterOptions.OnRejected</c>, which is
    /// a single global property: a host that also calls <c>AddRateLimiter</c> and sets its own
    /// <c>OnRejected</c> would silently replace BlogIt's (or have its own replaced), depending only on
    /// which registration ran last. Per-policy handlers are consulted for the endpoint's own policy,
    /// so BlogIt keeps its <c>429</c> and the host keeps whatever it chose for its own policies.
    /// </para>
    /// <para>
    /// Nothing here can make double-counting harmless when two rate limiter middlewares are in one
    /// pipeline — that is what <see cref="BlogItPipelineOptions.AddRateLimiterMiddleware"/> is for. A
    /// per-request "already counted" guard in <see cref="GetPartition"/> was implemented and rejected:
    /// the framework invokes the partitioner more than once per request, so the guard turned every
    /// limited request into a no-limiter partition and disabled rate limiting outright (caught by
    /// <c>RateLimiterEnforcementTests</c>).
    /// </para>
    /// </remarks>
    private sealed class Policy(string policyName) : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } =
            static (context, _) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return ValueTask.CompletedTask;
            };

        // Both lookups run per request rather than being resolved once in the constructor, so the
        // switch expressions above stay the single place each policy's limit and partitioning are
        // stated — which is also what makes them assertable without sending traffic.
        public RateLimitPartition<string> GetPartition(HttpContext httpContext) =>
            RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(policyName, httpContext),
                _ => Limits(policyName));
    }

    private static FixedWindowRateLimiterOptions Window(int permitLimit, TimeSpan window) =>
        new() { PermitLimit = permitLimit, Window = window, QueueLimit = 0 };
}
