using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BlogIt;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace BlogIt.Tests.Integration;

/// <summary>
/// The embedding case BlogIt exists for: a host that already has its own authentication,
/// authorization and rate limiting, and then adds a blog to it.
/// </summary>
/// <remarks>
/// Every test here builds a real <c>WebApplication</c> over <c>TestServer</c> with the host's own
/// middleware registered <em>before</em> <c>UseBlogIt</c>, because that is the ordering a real app
/// uses and the ordering that used to throw at startup. A <c>WebApplicationFactory</c> over the
/// sample host cannot cover this: the sample host has no auth of its own.
/// </remarks>
public sealed class HostOwnedMiddlewareTests
{
    private const string HostScheme = "HostCookieish";

    [Fact]
    public async Task UseBlogIt_DoesNotRejectAHostThatOwnsItsOwnAuthenticationAndAuthorization()
    {
        await using var host = await StartHostAsync();

        var response = await host.AdminClient().GetAsync("/api/users/");

        // 200, not 401: BlogIt's named-scheme policy still authenticates its own bearer tokens even
        // though the pipeline's authentication middleware belongs to the host.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HostAuthentication_StillWorksForTheHostsOwnEndpoints()
    {
        await using var host = await StartHostAsync();

        var response = await host.App.GetTestClient().GetAsync("/host/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("host-user");
    }

    [Fact]
    public async Task AuthMiddleware_RunsOncePerRequest_NotOncePerRegistration()
    {
        // The counters are what make "BlogIt added nothing on top" observable, and the authorization
        // one is the load-bearing half: a duplicated UseAuthentication is nearly free because
        // AuthenticationHandler caches its result for the request, but a duplicated UseAuthorization
        // evaluates the policy — every requirement handler, and for BlogIt endpoints the
        // security-stamp lookup behind its scheme — a second time for every request in the whole
        // application, blog or not. Verified by mutation: dropping the pipeline-mark detection makes
        // the authorization count 2 and leaves the authentication count at 1.
        await using var host = await StartHostAsync();

        (await host.App.GetTestClient().GetAsync("/host/whoami")).EnsureSuccessStatusCode();

        host.App.Services.GetRequiredService<HostAuthenticationCounter>().Count.Should().Be(1);
        host.App.Services.GetRequiredService<HostAuthorizationCounter>().Count.Should().Be(1);
    }

    [Fact]
    public async Task RateLimiting_StillEnforcesBlogItsPoliciesThroughTheHostsOwnMiddleware()
    {
        // The host owns UseRateLimiter here and BlogIt adds none of its own (see StartHostAsync), so
        // this proves the opt-out is survivable: BlogIt's per-endpoint policies are honoured by
        // whichever rate limiter middleware runs, and exactly one permit is charged per request —
        // where leaving both middlewares in would charge two and reject at half the configured limit.
        await using var host = await StartHostAsync();
        var client = host.App.GetTestClient();
        var request = new LoginRequest("nobody", "wrong-password");
        var permits = BlogItRateLimiterPolicies.Login.PermitLimit;

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < permits + 1; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", request);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(permits).Should().NotContain(HttpStatusCode.TooManyRequests);
        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task RateLimitRejection_KeepsBlogItsOwn429WhenTheHostConfiguredADifferentGlobalHandler()
    {
        // AddRateLimiter's OnRejected is one global property, so whoever registered last used to win
        // silently. BlogIt's policies now carry their own rejection handler.
        await using var host = await StartHostAsync();
        var client = host.App.GetTestClient();
        var request = new LoginRequest("nobody", "wrong-password");

        HttpResponseMessage? last = null;
        for (var attempt = 0; attempt < BlogItRateLimiterPolicies.Login.PermitLimit + 1; attempt++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", request);
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public void AddBlogIt_LeavesTheGlobalRateLimiterRejectionHandlerToTheHost()
    {
        // Asserted on the options rather than over HTTP because a host's global handler only shows
        // up on a *host* policy being rejected, which would mean standing up the host's own limited
        // endpoint and spending its budget.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new InMemoryDatabaseProvider($"Rejected_{Guid.NewGuid():N}"));
            options.UseFileSystemStorage(storage =>
                storage.RootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.OnRejected
            .Should().BeNull();
    }

    private static async Task<TestHost> StartHostAsync()
    {
        var storageRoot = Path.Combine(AppContext.BaseDirectory, $"host-owned-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new InMemoryDatabaseProvider($"HostOwned_{Guid.NewGuid():N}"));
            options.UseFileSystemStorage(storage => storage.RootPath = storageRoot);
        });

        // The host's own auth: a scheme that always succeeds, made the default so the default
        // authorization policy resolves against it and not against BlogIt's.
        builder.Services.AddSingleton<HostAuthenticationCounter>();
        builder.Services.AddSingleton<HostAuthorizationCounter>();
        builder.Services.AddSingleton<IAuthorizationHandler, CountingRequirementHandler>();
        builder.Services.AddAuthorizationBuilder()
            .SetDefaultPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new CountingRequirement())
                .Build());
        builder.Services.AddAuthentication(HostScheme)
            .AddScheme<AuthenticationSchemeOptions, AlwaysHostUserHandler>(HostScheme, _ => { });
        // The host's own rate limiter registration, complete with the global rejection handler that
        // used to be a coin flip between the two registrations.
        builder.Services.AddRateLimiter(options =>
        {
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return ValueTask.CompletedTask;
            };
            options.AddFixedWindowLimiter("host-policy", limiter =>
            {
                limiter.PermitLimit = 1;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });

        var app = builder.Build();
        await app.MigrateBlogItAsync();

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        // The whole point of the fixture: the host owns all three, so BlogIt contributes none of
        // them. Authentication and authorization need no flag — BlogIt sees the pipeline marks the
        // two calls above left behind. UseRateLimiter leaves no such mark, so it needs the flag.
        app.UseBlogIt(pipeline => pipeline.AddRateLimiterMiddleware = false);
        app.MapBlogIt();
        app.MapGet("/host/whoami", (HttpContext context) => context.User.Identity!.Name)
            .RequireAuthorization();

        await app.StartAsync();

        var userId = Guid.NewGuid();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
            db.Users.Add(new AppUser
            {
                Id = userId,
                Username = "admin-user",
                DisplayName = "Admin User",
                PasswordHash = "unused",
                SecurityStamp = BlogItSampleFactory.DefaultTestSecurityStamp
            });
            db.SiteSettings.Add(new SiteSetting
            {
                Key = SettingKeys.JwtSecret,
                Value = BlogItSampleFactory.TestJwtSecret
            });
            await db.SaveChangesAsync();
        }

        return new TestHost(app, userId, storageRoot);
    }

    private sealed record TestHost(WebApplication App, Guid AdminUserId, string StorageRoot)
        : IAsyncDisposable
    {
        public HttpClient AdminClient() => App.GetTestClient().WithAuth(AdminUserId, "admin-user");

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            if (Directory.Exists(StorageRoot))
                Directory.Delete(StorageRoot, recursive: true);
        }
    }

    /// <summary>Counts how many times the host's default authorization policy was evaluated.</summary>
    private sealed class HostAuthorizationCounter
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Increment() => Interlocked.Increment(ref count);
    }

    private sealed class CountingRequirement : IAuthorizationRequirement;

    private sealed class CountingRequirementHandler(HostAuthorizationCounter counter)
        : AuthorizationHandler<CountingRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            CountingRequirement requirement)
        {
            counter.Increment();
            context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    /// <summary>Counts how many times the host's scheme was asked to authenticate.</summary>
    private sealed class HostAuthenticationCounter
    {
        private int count;

        public int Count => Volatile.Read(ref count);

        public void Increment() => Interlocked.Increment(ref count);
    }

    private sealed class AlwaysHostUserHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        HostAuthenticationCounter counter)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            counter.Increment();
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "host-user")],
                HostScheme);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), HostScheme)));
        }
    }

    private sealed class InMemoryDatabaseProvider(string databaseName)
        : IBlogItDatabaseProviderRegistration
    {
        public string Name => "test-in-memory";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddDbContextFactory<BlogItDbContext>(
                options => options.UseInMemoryDatabase(databaseName));
            services.AddSingleton<IBlogItMigrator, EnsureCreatedMigrator>();
        }
    }

    private sealed class EnsureCreatedMigrator(
        IDbContextFactory<BlogItDbContext> factory) : IBlogItMigrator
    {
        public async Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
