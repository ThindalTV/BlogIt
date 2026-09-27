using System.Net;
using System.Threading.RateLimiting;
using BlogIt.Shared.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers <see cref="BlogItRateLimiterPolicies"/> — the limits themselves, the partition keys, and
/// which endpoints each policy is attached to.
/// </summary>
/// <remarks>
/// <para>
/// The limits are asserted by driving a real <see cref="FixedWindowRateLimiter"/> built from the
/// same options object the policy hands to the middleware, rather than by sending eleven HTTP
/// requests: <c>RateLimiterOptions</c> keeps its policy map internal to ASP.NET Core, so the
/// alternative would have been hammering every endpoint, which is slow, order-dependent across a
/// shared <c>WebApplicationFactory</c>, and still would not tell us the partition key.
/// </para>
/// <para>
/// End-to-end enforcement — that <c>UseRateLimiter</c> is in the pipeline and rejection surfaces as
/// <c>429</c> — is covered separately by <c>RateLimiterEnforcementTests</c>.
/// </para>
/// </remarks>
public class RateLimiterPolicyTests
{
    [Fact]
    public void Login_KeepsTenAttemptsPerFiveMinutes()
    {
        BlogItRateLimiterPolicies.Login.PermitLimit.Should().Be(10);
        BlogItRateLimiterPolicies.Login.Window.Should().Be(TimeSpan.FromMinutes(5));
        BlogItRateLimiterPolicies.Login.QueueLimit.Should().Be(0);
    }

    [Fact]
    public void Account_AllowsTwentyCredentialOperationsPerFiveMinutes()
    {
        // 4 a minute is useless for guessing a current password and still covers an admin
        // onboarding a small team in one sitting.
        BlogItRateLimiterPolicies.Account.PermitLimit.Should().Be(20);
        BlogItRateLimiterPolicies.Account.Window.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void Setup_AllowsSixtyProbesPerMinute()
    {
        BlogItRateLimiterPolicies.Setup.PermitLimit.Should().Be(60);
        BlogItRateLimiterPolicies.Setup.Window.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void Media_IsSizedForRealPageLoadsRatherThanForAdminTraffic()
    {
        // The number that matters most: /media is hit once per image per page view by ordinary
        // visitors. A 60-image gallery post must load, several times over, without tripping.
        BlogItRateLimiterPolicies.Media.PermitLimit.Should().Be(600);
        BlogItRateLimiterPolicies.Media.Window.Should().Be(TimeSpan.FromMinutes(1));
        // The arithmetic the number came from, written down: a 60-image gallery post, loaded five
        // times over inside one window, must still be under the limit.
        BlogItRateLimiterPolicies.Media.PermitLimit.Should().BeGreaterThanOrEqualTo(
            60 * 5,
            "a visitor-facing asset route cannot be sized like an admin endpoint");
    }

    [Fact]
    public void RootDocument_AllowsThirtyCrawlerFetchesPerMinute()
    {
        BlogItRateLimiterPolicies.RootDocument.PermitLimit.Should().Be(30);
        BlogItRateLimiterPolicies.RootDocument.Window.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void EveryPolicyRejectsRatherThanQueues()
    {
        // A queue would turn a flood into latency for everyone else instead of a 429 for the
        // flooder, which defeats the point of limiting these routes at all.
        AllPolicies().Should().OnlyContain(policy => policy.QueueLimit == 0);
    }

    [Theory]
    [MemberData(nameof(PolicyNames))]
    public async Task Policy_AllowsExactlyItsPermitLimitWithinOneWindow(string name)
    {
        var options = BlogItRateLimiterPolicies.Limits(name);
        // AutoReplenishment off so the window cannot tick over mid-test and hand back a permit.
        await using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = options.PermitLimit,
            Window = options.Window,
            QueueLimit = options.QueueLimit,
            AutoReplenishment = false
        });

        for (var attempt = 1; attempt <= options.PermitLimit; attempt++)
        {
            using var lease = limiter.AttemptAcquire();
            lease.IsAcquired.Should().BeTrue($"attempt {attempt} is within the limit");
        }

        using var rejected = limiter.AttemptAcquire();
        rejected.IsAcquired.Should().BeFalse();
    }

    [Fact]
    public void Limits_RejectsANameThatIsNotOneOfThePolicies()
    {
        var limits = () => BlogItRateLimiterPolicies.Limits("BlogIt.NotAPolicy");
        var key = () => BlogItRateLimiterPolicies.PartitionKey("BlogIt.NotAPolicy", Context());

        limits.Should().Throw<ArgumentOutOfRangeException>();
        key.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ClientPartitionKey_IsTheCallersAddress()
    {
        // Per-partition, not global: one attacker must not be able to lock everyone else out.
        BlogItRateLimiterPolicies.ClientPartitionKey(Context("203.0.113.7"))
            .Should().Be("203.0.113.7");
        BlogItRateLimiterPolicies.ClientPartitionKey(Context("203.0.113.7"))
            .Should().NotBe(BlogItRateLimiterPolicies.ClientPartitionKey(Context("203.0.113.8")));
    }

    [Fact]
    public void ClientPartitionKey_FallsBackToASharedBucketWithoutAnAddress()
    {
        BlogItRateLimiterPolicies.ClientPartitionKey(Context(address: null))
            .Should().Be("unknown");
    }

    [Fact]
    public void SessionPartitionKey_TracksTheBearerTokenSoRotatingAddressesDoNotEvadeIt()
    {
        // The threat the account policy answers is a *stolen token* guessing the current password.
        // Partitioning on the address alone would let the holder reset the counter by moving; the
        // token is the thing they cannot change.
        var first = BlogItRateLimiterPolicies.SessionPartitionKey(
            Context("203.0.113.7", bearer: "token-alpha"));
        var moved = BlogItRateLimiterPolicies.SessionPartitionKey(
            Context("198.51.100.9", bearer: "token-alpha"));
        var other = BlogItRateLimiterPolicies.SessionPartitionKey(
            Context("203.0.113.7", bearer: "token-beta"));

        moved.Should().Be(first);
        other.Should().NotBe(first);
    }

    [Fact]
    public void SessionPartitionKey_DoesNotCarryTheTokenItself()
    {
        // Partition keys end up in diagnostics; a bearer token is a credential and must not.
        var key = BlogItRateLimiterPolicies.SessionPartitionKey(
            Context("203.0.113.7", bearer: "super-secret-token"));

        key.Should().NotContain("super-secret-token");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer")]
    [InlineData("Bearer    ")]
    public void SessionPartitionKey_FallsBackToTheAddressWithoutAUsableBearerToken(string? header)
    {
        var context = Context("203.0.113.7");
        if (header is not null)
            context.Request.Headers.Authorization = header;

        BlogItRateLimiterPolicies.SessionPartitionKey(context).Should().Be("203.0.113.7");
    }

    [Fact]
    public void PartitionKey_UsesTheSessionOnlyForTheAccountPolicy()
    {
        // A bearer token is present throughout, so anything still keyed on the address is provably
        // using ClientPartitionKey rather than falling back to it.
        var context = Context("203.0.113.7", bearer: "token-alpha");

        BlogItRateLimiterPolicies.PartitionKey(BlogItDefaults.LoginRateLimiterPolicy, context)
            .Should().Be("203.0.113.7");
        BlogItRateLimiterPolicies.PartitionKey(BlogItDefaults.SetupRateLimiterPolicy, context)
            .Should().Be("203.0.113.7");
        BlogItRateLimiterPolicies.PartitionKey(BlogItDefaults.MediaRateLimiterPolicy, context)
            .Should().Be("203.0.113.7");
        BlogItRateLimiterPolicies.PartitionKey(BlogItDefaults.RootDocumentRateLimiterPolicy, context)
            .Should().Be("203.0.113.7");
        BlogItRateLimiterPolicies.PartitionKey(BlogItDefaults.AccountRateLimiterPolicy, context)
            .Should().Be(BlogItRateLimiterPolicies.SessionPartitionKey(context))
            .And.NotBe("203.0.113.7");
    }

    [Fact]
    public void Names_CoverEveryPolicyTheEndpointsAskFor()
    {
        BlogItRateLimiterPolicies.Names.Should().BeEquivalentTo(
            BlogItDefaults.LoginRateLimiterPolicy,
            BlogItDefaults.AccountRateLimiterPolicy,
            BlogItDefaults.SetupRateLimiterPolicy,
            BlogItDefaults.MediaRateLimiterPolicy,
            BlogItDefaults.RootDocumentRateLimiterPolicy);
    }

    [Theory]
    [InlineData("/api/auth/login", "POST", BlogItDefaults.LoginRateLimiterPolicy)]
    [InlineData("/api/auth/change-password", "POST", BlogItDefaults.AccountRateLimiterPolicy)]
    [InlineData("/api/users/", "POST", BlogItDefaults.AccountRateLimiterPolicy)]
    [InlineData("/api/setup/status", "GET", BlogItDefaults.SetupRateLimiterPolicy)]
    [InlineData("/api/setup/initialize", "POST", BlogItDefaults.SetupRateLimiterPolicy)]
    [InlineData("/media/{**path}", "GET", BlogItDefaults.MediaRateLimiterPolicy)]
    [InlineData("/sitemap.xml", "GET", BlogItDefaults.RootDocumentRateLimiterPolicy)]
    [InlineData("/robots.txt", "GET", BlogItDefaults.RootDocumentRateLimiterPolicy)]
    [InlineData("/rss.xml", "GET", BlogItDefaults.RootDocumentRateLimiterPolicy)]
    [InlineData("/atom.xml", "GET", BlogItDefaults.RootDocumentRateLimiterPolicy)]
    public async Task MapBlogIt_AttachesTheExpectedPolicyToEachThrottledEndpoint(
        string pattern,
        string method,
        string expectedPolicy)
    {
        var endpoints = await MapEndpointsAsync();

        var endpoint = endpoints.Should().ContainSingle(candidate =>
            candidate.RoutePattern.RawText == pattern
            && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!
                .HttpMethods.Contains(method)).Subject;

        // Not `GetMetadata<…>()?.PolicyName`: with the null-conditional the whole assertion
        // evaporates when the metadata is absent, which is the exact case under test.
        endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()
            .Should().NotBeNull()
            .And.Subject.As<EnableRateLimitingAttribute>().PolicyName
            .Should().Be(expectedPolicy);
    }

    [Fact]
    public async Task MapBlogIt_LeavesReadOnlyAdminRoutesUnthrottled()
    {
        // Not an oversight: those routes already require a valid admin token, cost a single indexed
        // query, and throttling them would cap the admin UI's own paging. Only the anonymous and
        // credential-touching routes are limited. Pinned so the distinction stays deliberate.
        var endpoints = await MapEndpointsAsync();

        var listUsers = endpoints.Single(candidate =>
            candidate.RoutePattern.RawText == "/api/users/"
            && candidate.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Contains("GET"));

        listUsers.Metadata.GetMetadata<EnableRateLimitingAttribute>().Should().BeNull();
    }

    public static TheoryData<string> PolicyNames() => new(BlogItRateLimiterPolicies.Names);

    private static IEnumerable<FixedWindowRateLimiterOptions> AllPolicies() =>
        BlogItRateLimiterPolicies.Names.Select(BlogItRateLimiterPolicies.Limits);

    private static DefaultHttpContext Context(string? address = "203.0.113.7", string? bearer = null)
    {
        var context = new DefaultHttpContext();
        if (address is not null)
            context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        if (bearer is not null)
            context.Request.Headers.Authorization = $"Bearer {bearer}";
        return context;
    }

    private static async Task<List<RouteEndpoint>> MapEndpointsAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseStorageProvider(new FakeStorageProvider());
        });
        await using var app = builder.Build();

        app.MapBlogIt();

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private sealed record FakeDatabaseProvider : IBlogItDatabaseProviderRegistration
    {
        public string Name => "fake-db";

        public void RegisterServices(IServiceCollection services) =>
            services.AddDbContextFactory<BlogItDbContext>(options =>
                options.UseInMemoryDatabase($"RateLimiter_{Guid.NewGuid():N}"));
    }

    private sealed record FakeStorageProvider : IBlogItStorageProviderRegistration
    {
        public string Name => "fake-storage";

        public void RegisterServices(IServiceCollection services) =>
            services.AddSingleton<IBlogItMediaStorage, FakeStorage>();
    }

    private sealed class FakeStorage : IBlogItMediaStorage
    {
        public Task<string> StoreAsync(
            Stream content,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<Stream?> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
