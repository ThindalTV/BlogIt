using BlogIt.MauiAdmin.Core.Sites;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// A site profile is collected as a bare domain and an optional port, and every request the app
/// makes is built from it. These pin the URL assembly, because a profile that produces the wrong
/// base address fails as an authentication error rather than as anything that points at the URL.
/// </summary>
public class SiteProfileTests
{
    [Fact]
    public void HttpsWithNoPortUsesTheDefaultPort()
    {
        var profile = new SiteProfile { Host = "myblog.com" };

        profile.BaseUri.Should().Be(new Uri("https://myblog.com/"));
    }

    [Fact]
    public void AnExplicitPortIsCarriedIntoTheBaseUri()
    {
        var profile = new SiteProfile { Host = "localhost", Port = 5001 };

        profile.BaseUri.Should().Be(new Uri("https://localhost:5001/"));
    }

    [Fact]
    public void PlainHttpIsSupportedForLocalDevelopment()
    {
        var profile = new SiteProfile { Host = "localhost", Port = 5000, UseHttps = false };

        profile.BaseUri.Should().Be(new Uri("http://localhost:5000/"));
    }

    [Fact]
    public void ApiPathDefaultsToTheServerDefault()
    {
        new SiteProfile { Host = "myblog.com" }.ApiPath.Should().Be("/api");
    }

    [Fact]
    public void ApiPathCanBeOverriddenForACustomisedServer()
    {
        var profile = new SiteProfile { Host = "myblog.com", ApiPathOverride = "/admin-api" };

        profile.ApiPath.Should().Be("/admin-api");
    }

    [Fact]
    public void ABlankOverrideFallsBackToTheDefault()
    {
        var profile = new SiteProfile { Host = "myblog.com", ApiPathOverride = "   " };

        profile.ApiPath.Should().Be("/api", "an empty box in the form means 'unset', not 'no prefix'");
    }

    [Fact]
    public void TheDisplayLabelFallsBackToTheHostWhenUnnamed()
    {
        new SiteProfile { Host = "myblog.com" }.DisplayLabel.Should().Be("myblog.com");
        new SiteProfile { Host = "myblog.com", Name = "Travel" }.DisplayLabel.Should().Be("Travel");
    }

    [Fact]
    public void ATokenIsOnlyValidWhileItHasUsefulLifeLeft()
    {
        var expired = new SiteProfile
        {
            Host = "myblog.com",
            HasStoredToken = true,
            TokenExpiresAt = DateTime.UtcNow.AddMinutes(-1),
        };
        var expiringNow = new SiteProfile
        {
            Host = "myblog.com",
            HasStoredToken = true,
            TokenExpiresAt = DateTime.UtcNow.AddSeconds(30),
        };
        var valid = new SiteProfile
        {
            Host = "myblog.com",
            HasStoredToken = true,
            TokenExpiresAt = DateTime.UtcNow.AddHours(1),
        };

        expired.IsTokenValid.Should().BeFalse();
        expiringNow.IsTokenValid.Should().BeFalse(
            "a token with seconds left would expire mid-request, so it counts as expired");
        valid.IsTokenValid.Should().BeTrue();
    }

    [Fact]
    public void NoStoredTokenIsNeverValidHoweverFarOffTheExpiry()
    {
        var profile = new SiteProfile
        {
            Host = "myblog.com",
            HasStoredToken = false,
            TokenExpiresAt = DateTime.UtcNow.AddYears(1),
        };

        profile.IsTokenValid.Should().BeFalse();
    }
}
