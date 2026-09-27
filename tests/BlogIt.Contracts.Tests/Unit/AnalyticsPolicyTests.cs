using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Direct coverage for <see cref="AnalyticsPolicy"/>: the shape of a Google Tag Manager container
/// ID, and the rule that ties GA4 reporting to the presence of one. The integration tests exercise
/// it through the two write paths; these pin the rule itself, including the halves that are easy to
/// get wrong — either reporting field on its own counts as reporting, whitespace is not a container
/// ID, and a GA4 measurement ID is not one either.
/// </summary>
public class AnalyticsPolicyTests
{
    private const string Credentials = @"{""type"":""service_account""}";

    [Fact]
    public void Validate_AllowsAContainerWithNoReporting()
    {
        // Tracking without reporting is an ordinary configuration: the container collects, and
        // nothing reads it back into the dashboard.
        AnalyticsPolicy.Validate("GTM-WQCQZBKK", null, null).Should().BeNull();
    }

    [Fact]
    public void Validate_AllowsNothingConfiguredAtAll()
    {
        AnalyticsPolicy.Validate(null, null, null).Should().BeNull();
    }

    [Fact]
    public void Validate_AllowsReportingAlongsideAContainer()
    {
        AnalyticsPolicy.Validate("GTM-WQCQZBKK", "123456789", Credentials).Should().BeNull();
    }

    [Theory]
    [InlineData("123456789", null)]
    [InlineData(null, Credentials)]
    [InlineData("123456789", Credentials)]
    public void Validate_RejectsReportingWithoutAContainer(string? propertyId, string? credentialsJson)
    {
        // Either field alone is enough to reject: half-configured reporting is still reporting the
        // admin will try to use, and it is still reporting on traffic no container consented to
        // collect.
        AnalyticsPolicy.Validate(null, propertyId, credentialsJson)
            .Should().Be(AnalyticsPolicy.ReportingRequiresTagMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_TreatsABlankContainerIdAsNoTag(string blank)
    {
        // "Set to nothing" and "never set" are one state here, matching how ISettingsService
        // documents null-versus-empty for every other optional setting. Blank is also not
        // *malformed* — it has to reach the reporting rule, not the shape one.
        AnalyticsPolicy.HasTag(blank).Should().BeFalse();
        AnalyticsPolicy.IsWellFormedContainerId(blank).Should().BeTrue();
        AnalyticsPolicy.Validate(blank, "123456789", null)
            .Should().Be(AnalyticsPolicy.ReportingRequiresTagMessage);
    }

    [Theory]
    [InlineData("G-ABC123")]          // a GA4 measurement ID: the mistake worth catching
    [InlineData("GTM-")]              // prefix with no container
    [InlineData("WQCQZBKK")]          // the ID without its prefix
    [InlineData("GTM-ABC 123")]       // a space, from a sloppy paste
    [InlineData("GTM-ABC-123")]       // punctuation real container IDs do not carry
    public void Validate_RejectsAnythingNotShapedLikeAContainerId(string malformed)
    {
        AnalyticsPolicy.IsWellFormedContainerId(malformed).Should().BeFalse();
        AnalyticsPolicy.Validate(malformed, null, null)
            .Should().Be(AnalyticsPolicy.MalformedContainerIdMessage);
    }

    [Fact]
    public void Validate_ReportsAMalformedContainerIdBeforeTheReportingRule()
    {
        // Both rules are broken here. Naming the shape problem is the useful half: told only that
        // reporting needs a container ID, the reader would be staring at one that is already filled
        // in.
        AnalyticsPolicy.Validate("G-ABC123", "123456789", Credentials)
            .Should().Be(AnalyticsPolicy.MalformedContainerIdMessage);
    }

    [Theory]
    [InlineData("gtm-wqcqzbkk")]
    [InlineData("  GTM-WQCQZBKK  ")]
    public void IsWellFormedContainerId_AcceptsCasingAndSurroundingWhitespace(string containerId)
    {
        // Both write paths trim before storing, and the ID is not case-sensitive to Google, so
        // rejecting either would only punish a paste from the GTM console.
        AnalyticsPolicy.IsWellFormedContainerId(containerId).Should().BeTrue();
    }
}
