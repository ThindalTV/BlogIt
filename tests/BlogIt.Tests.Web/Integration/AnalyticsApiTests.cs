using System.Net;
using BlogIt.Services;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Pins how <c>AnalyticsApi</c> tells the three analytics outcomes apart: nothing configured, a
/// configuration the operator has to fix, and a provider call that failed.
/// </summary>
/// <remarks>
/// Written against a substituted <see cref="IAnalyticsService"/> rather than the real Google
/// provider, because the distinction being pinned is the endpoint's, and the provider's own half
/// lives in a satellite package with no offline-testable transport. The provider's side is covered
/// by <c>GoogleAnalyticsServiceTests</c>.
/// </remarks>
public sealed class AnalyticsApiTests(AnalyticsApiTests.AnalyticsFactory factory)
    : IClassFixture<AnalyticsApiTests.AnalyticsFactory>
{
    [Fact]
    public async Task Summary_ReturnsTheProvidersData()
    {
        factory.AnalyticsService.Behaviour =
            () => new AnalyticsSummaryDto(3, 2, 1, [new TopPageDto("/hello", 1)]);
        var client = await AuthenticatedClientAsync("analytics-ok");

        var response = await client.GetAsync("/api/analytics/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Summary_ReportsNotConfiguredWhenTheProviderHasNoSummary()
    {
        factory.AnalyticsService.Behaviour = () => null;
        var client = await AuthenticatedClientAsync("analytics-none");

        var response = await client.GetAsync("/api/analytics/summary");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("Analytics is not configured.");
    }

    [Fact]
    public async Task Summary_EchoesAConfigurationFailureSoTheOperatorCanFixIt()
    {
        factory.AnalyticsService.Behaviour = () =>
            throw new InvalidOperationException("The service-account JSON could not be read.");
        var client = await AuthenticatedClientAsync("analytics-broken");

        var response = await client.GetAsync("/api/analytics/summary");

        // 400 with the message echoed, matching AiApi.HandleAiFailure: this is the one class of
        // failure whose text is written to be shown to an admin, and it must not be confused with
        // the 404 above - "your credentials are broken" is not "you have not set this up".
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync())
            .Should().Contain("service-account JSON could not be read");
    }

    [Fact]
    public async Task Summary_ReportsAFailedProviderCallWithoutLeakingItsDetail()
    {
        factory.AnalyticsService.Behaviour = () =>
            throw new HttpRequestException("connect ETIMEDOUT 142.250.74.10:443");
        var client = await AuthenticatedClientAsync("analytics-upstream");

        var response = await client.GetAsync("/api/analytics/summary");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("analytics request failed");
        // The upstream message can carry internals, so it goes to the log and not to the response.
        body.Should().NotContain("ETIMEDOUT");
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string prefix) =>
        factory.CreateClient().WithAuth(await factory.SeedUserAsync($"{prefix}-{Guid.NewGuid():N}"));

    public sealed class AnalyticsFactory : BlogItSampleFactory
    {
        public FakeAnalyticsService AnalyticsService { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IAnalyticsService>();
                services.AddSingleton<IAnalyticsService>(AnalyticsService);
            });
        }
    }

    public sealed class FakeAnalyticsService : IAnalyticsService
    {
        public Func<AnalyticsSummaryDto?> Behaviour { get; set; } = () => null;

        public Task<AnalyticsSummaryDto?> GetSummaryAsync(string startDate, string endDate) =>
            Task.FromResult(Behaviour());
    }
}
