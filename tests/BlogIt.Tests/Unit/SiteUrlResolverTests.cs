using BlogIt.Shared.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Direct coverage for <see cref="SiteUrlResolver"/>. It decides what absolute URL goes into every
/// crawler-facing document, and its last resort is the attacker-controllable <c>Host</c> header, so
/// the precedence order and the no-request path both need pinning rather than inferring from the
/// callers that happen to exercise one branch each.
/// </summary>
public class SiteUrlResolverTests
{
    [Fact]
    public void Resolve_PrefersTheOperatorConfiguredSettingsValue()
    {
        var resolved = SiteUrlResolver.Resolve(
            "https://settings.example/",
            "https://configuration.example/",
            Request("https://request.example"));

        resolved.Should().Be("https://settings.example/");
    }

    [Fact]
    public void Resolve_FallsBackToConfigurationWhenSettingsAreUnset()
    {
        var resolved = SiteUrlResolver.Resolve(
            null,
            "https://configuration.example/",
            Request("https://request.example"));

        resolved.Should().Be("https://configuration.example/");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/relative")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://settings.example/")]
    public void Resolve_SkipsAnUnusableSettingsValueInsteadOfFailing(string settingsValue)
    {
        // Falls through rather than throwing: a bad value in one source must not take the site's
        // crawler documents down when a good value exists further along the chain.
        var resolved = SiteUrlResolver.Resolve(
            settingsValue,
            "https://configuration.example/",
            request: null);

        resolved.Should().Be("https://configuration.example/");
    }

    [Theory]
    [InlineData("https://settings.example", "https://settings.example/")]
    [InlineData("https://settings.example/", "https://settings.example/")]
    [InlineData("https://settings.example///", "https://settings.example/")]
    [InlineData("  https://settings.example/blog  ", "https://settings.example/blog/")]
    [InlineData("https://settings.example/blog/", "https://settings.example/blog/")]
    [InlineData("https://settings.example:8443/blog", "https://settings.example:8443/blog/")]
    public void Resolve_NormalisesToExactlyOneTrailingSlash(string configured, string expected) =>
        SiteUrlResolver.Resolve(configured, null, request: null).Should().Be(expected);

    [Fact]
    public void Resolve_DropsAQueryOrFragmentFromAConfiguredValue()
    {
        // Only the left part up to the path is kept: a site URL carrying a query string would be
        // concatenated with post paths and produce nonsense.
        SiteUrlResolver.Resolve("https://settings.example/blog?utm=1#top", null, request: null)
            .Should().Be("https://settings.example/blog/");
    }

    [Fact]
    public void Resolve_FallsBackToTheUntrustedHostHeaderWhenNothingIsConfigured()
    {
        // Deliberately pinned, including the fact that the value is whatever the client sent: this
        // is the branch that makes an unconfigured site emit attacker-chosen URLs, and a change to
        // it should be a decision rather than a silent side effect. Configuring a site URL is what
        // takes this branch out of play.
        var resolved = SiteUrlResolver.Resolve(
            null,
            null,
            Request("https://evil.example"));

        resolved.Should().Be("https://evil.example/");
    }

    [Fact]
    public void Resolve_KeepsThePathBaseWhenFallingBackToTheRequest()
    {
        var resolved = SiteUrlResolver.Resolve(null, null, Request("https://host.example", "/blog"));

        resolved.Should().Be("https://host.example/blog/");
    }

    [Fact]
    public void Resolve_ThrowsWhenNothingIsConfiguredAndThereIsNoRequest()
    {
        // The no-request path: a background job or a cache warmup calling ISiteMetadataService has
        // no Host header to borrow, and must fail loudly rather than emit relative or bogus URLs.
        var resolve = () => SiteUrlResolver.Resolve(null, null, request: null);

        resolve.Should().Throw<InvalidOperationException>()
            .WithMessage("*site URL or request origin is required*");
    }

    [Fact]
    public void Resolve_ThrowsWhenTheRequestOriginIsNotUsableEither()
    {
        var request = Request("https://host.example");
        request.Scheme = "ftp";

        var resolve = () => SiteUrlResolver.Resolve(null, null, request);

        resolve.Should().Throw<InvalidOperationException>();
    }

    private static HttpRequest Request(string origin, string pathBase = "")
    {
        var uri = new Uri(origin);
        var context = new DefaultHttpContext();
        context.Request.Scheme = uri.Scheme;
        context.Request.Host = new HostString(uri.Authority);
        context.Request.PathBase = pathBase;
        return context.Request;
    }
}
