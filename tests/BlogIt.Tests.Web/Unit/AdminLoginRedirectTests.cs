using BlogIt.Admin.Services;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Both ends of the returnUrl round trip: the 401 handler writes it, the login page reads it back.
/// </summary>
public class AdminLoginRedirectTests
{
    [Theory]
    [InlineData("posts/9f1c", "login?returnUrl=posts%2F9f1c")]
    [InlineData("media", "login?returnUrl=media")]
    [InlineData("posts?q=hello world", "login?returnUrl=posts%3Fq%3Dhello%20world")]
    public void BuildLoginUrl_CarriesTheInterruptedPage(string current, string expected)
        => AdminLoginRedirect.BuildLoginUrl(current).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("login")]
    [InlineData("login?returnUrl=posts")]
    [InlineData("setup")]
    public void BuildLoginUrl_OmitsReturnUrlWhereItWouldBePointless(string? current)
        => AdminLoginRedirect.BuildLoginUrl(current).Should().Be("login");

    [Theory]
    [InlineData("posts/9f1c", "posts/9f1c")]
    [InlineData("media", "media")]
    public void ResolveReturnPath_AcceptsRelativeAdminPaths(string returnUrl, string expected)
        => AdminLoginRedirect.ResolveReturnPath(returnUrl).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/absolute/path")]
    [InlineData("javascript:alert(1)")]
    public void ResolveReturnPath_FallsBackToDashboardForAnythingNotRelative(string? returnUrl)
        => AdminLoginRedirect.ResolveReturnPath(returnUrl).Should().BeEmpty();
}
