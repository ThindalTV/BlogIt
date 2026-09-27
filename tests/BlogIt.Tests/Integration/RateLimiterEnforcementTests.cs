using System.Net;
using System.Net.Http.Json;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Proves the rate limiter is actually in the request pipeline and that exceeding a policy surfaces
/// as <c>429</c> rather than being counted and ignored.
/// </summary>
/// <remarks>
/// <para>
/// Only one policy is driven end to end, and deliberately the cheapest one: login, at 10 attempts
/// per 5 minutes. The middleware is shared by every policy, so what this test adds over
/// <c>RateLimiterPolicyTests</c> is "<c>UseRateLimiter</c> runs and <c>OnRejected</c> writes 429" —
/// a property of the pipeline, not of any one policy. Repeating it for <c>/media</c> would mean 601
/// requests to learn nothing new.
/// </para>
/// <para>
/// This class owns its own factory (xUnit gives each test class its own <c>IClassFixture</c>
/// instance) because the limiter is a singleton in that factory's container: running it against a
/// shared factory would spend other tests' login budget.
/// </para>
/// </remarks>
public class RateLimiterEnforcementTests(BlogItSampleFactory factory)
    : IClassFixture<BlogItSampleFactory>
{
    [Fact]
    public async Task Login_StartsReturningTooManyRequests_OnceThePolicysPermitsAreSpent()
    {
        await factory.SeedUserAsync("throttled", "P@ssw0rd!");
        var client = factory.CreateClient();
        // Wrong password on purpose: the point is that failed guesses are what gets throttled, and
        // it also keeps the test from depending on a successful login's side effects.
        var request = new LoginRequest("throttled", "wrong-password");

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < BlogItRateLimiterPolicies.Login.PermitLimit + 1; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", request);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(BlogItRateLimiterPolicies.Login.PermitLimit)
            .Should().AllBeEquivalentTo(HttpStatusCode.Unauthorized);
        statuses[^1].Should().Be(HttpStatusCode.TooManyRequests);
    }
}
