using System.Net;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using Bunit;
using FluentAssertions;
using DashboardPage = BlogIt.Admin.Pages.Dashboard;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers the dashboard's analytics panel, which used to swallow every failure into the same
/// "not configured or no data available" line — so a site whose analytics credentials had gone bad
/// looked identical to one that had never set analytics up.
/// </summary>
public sealed class AdminDashboardAnalyticsTests
{
    [Fact]
    public void AnalyticsPanel_ReportsNotConfiguredWhenTheServerHasNoSummary()
    {
        // 404 is the not-configured answer, with or without an analytics package installed.
        using var ctx = Harness().RouteStatus("analytics/summary", HttpStatusCode.NotFound);

        var markup = ctx.Render<DashboardPage>().Markup;

        markup.Should().Contain("Analytics not configured");
    }

    [Fact]
    public void AnalyticsPanel_ShowsWhatToFixWhenTheProviderIsMisconfigured()
    {
        using var ctx = Harness().RouteJson(
            "analytics/summary",
            HttpStatusCode.BadRequest,
            """{"status":400,"detail":"The Google Analytics service-account JSON could not be read."}""");

        var markup = ctx.Render<DashboardPage>().Markup;

        markup.Should().Contain("service-account JSON could not be read");
        markup.Should().NotContain("Analytics not configured");
    }

    [Fact]
    public void AnalyticsPanel_ReportsAFailedProviderCallRatherThanLookingUnconfigured()
    {
        using var ctx = Harness().RouteJson(
            "analytics/summary",
            HttpStatusCode.BadGateway,
            """{"status":502,"detail":"The analytics request failed. Please try again."}""");

        var markup = ctx.Render<DashboardPage>().Markup;

        markup.Should().Contain("analytics request failed");
        markup.Should().NotContain("Analytics not configured");
    }

    // The three counters the dashboard loads alongside analytics; unrouted they 404 into the
    // page's own error banner, which would drown out what these tests are asserting.
    private static AdminComponentHarness Harness() =>
        new AdminComponentHarness()
            .Route("posts", AdminComponentHarness.OnePage<BlogPostSummaryDto>())
            .Route("pages", AdminComponentHarness.OnePage<PageDto>())
            .Route("media", AdminComponentHarness.OnePage<MediaFileDto>());
}
