using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Direct coverage for <see cref="UrlValidator"/>. It guards operator-supplied URLs that end up in
/// crawler documents and in admin markup, so the scheme allow-list — not just "is it a URL" — is
/// the property that matters.
/// </summary>
public class UrlValidatorTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/blog/")]
    [InlineData("https://example.com:8443/blog")]
    [InlineData("HTTPS://EXAMPLE.COM")]
    public void IsValidAbsoluteHttpUrl_AcceptsAbsoluteHttpAndHttpsUrls(string value) =>
        UrlValidator.IsValidAbsoluteHttpUrl(value).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValidAbsoluteHttpUrl_RejectsMissingValues(string? value) =>
        UrlValidator.IsValidAbsoluteHttpUrl(value).Should().BeFalse();

    [Theory]
    [InlineData("/blog")]
    [InlineData("example.com")]
    [InlineData("//example.com")]
    public void IsValidAbsoluteHttpUrl_RejectsAnythingNotAbsolute(string value) =>
        UrlValidator.IsValidAbsoluteHttpUrl(value).Should().BeFalse();

    [Theory]
    // The whole reason the scheme is checked rather than just parsed: every one of these is a
    // perfectly well-formed absolute URI, and every one of them is an XSS or an exfiltration
    // vector once it reaches an href.
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com")]
    [InlineData("mailto:someone@example.com")]
    public void IsValidAbsoluteHttpUrl_RejectsNonHttpSchemes(string value) =>
        UrlValidator.IsValidAbsoluteHttpUrl(value).Should().BeFalse();

    [Theory]
    // Loopback, in every spelling the framework accepts.
    [InlineData("http://localhost/v1")]
    [InlineData("http://LOCALHOST:8080/v1")]
    [InlineData("http://api.localhost/v1")]
    [InlineData("http://127.0.0.1/v1")]
    [InlineData("http://127.9.9.9/v1")]
    [InlineData("http://[::1]/v1")]
    // The cloud metadata address this finding names, and the rest of link-local.
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[fe80::1]/v1")]
    // RFC 1918, RFC 6598 carrier-grade NAT, IPv6 unique-local, and 0.0.0.0/8.
    [InlineData("http://10.1.2.3/v1")]
    [InlineData("http://172.16.0.1/v1")]
    [InlineData("http://172.31.255.255/v1")]
    [InlineData("http://192.168.0.1/v1")]
    [InlineData("http://100.64.0.1/v1")]
    [InlineData("http://[fd00::1]/v1")]
    [InlineData("http://0.0.0.0/v1")]
    // IPv4 written inside an IPv6 literal still reaches the same host.
    [InlineData("http://[::ffff:169.254.169.254]/v1")]
    // Names that can only mean something inside the deployment.
    [InlineData("http://ollama.local/v1")]
    [InlineData("http://models.internal/v1")]
    [InlineData("http://gateway.home.arpa/v1")]
    // A bare hostname with no dot cannot be a public name either.
    [InlineData("http://model-server/v1")]
    public void IsPrivateOrLocalHttpUrl_FlagsAddressesOnlyReachableFromInside(string value) =>
        UrlValidator.IsPrivateOrLocalHttpUrl(value).Should().BeTrue();

    [Theory]
    [InlineData("https://api.openai.com/v1")]
    [InlineData("https://models.inference.ai.azure.com")]
    [InlineData("http://8.8.8.8/v1")]
    [InlineData("http://172.32.0.1/v1")]
    [InlineData("http://11.0.0.1/v1")]
    [InlineData("http://100.128.0.1/v1")]
    public void IsPrivateOrLocalHttpUrl_LeavesPublicEndpointsAlone(string value) =>
        UrlValidator.IsPrivateOrLocalHttpUrl(value).Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    public void IsPrivateOrLocalHttpUrl_SaysNothingAboutValuesItCannotParse(string? value) =>
        // Scheme and shape are IsValidAbsoluteHttpUrl's job; this answers only "where does it point",
        // so an unparseable value is not this method's finding to report.
        UrlValidator.IsPrivateOrLocalHttpUrl(value).Should().BeFalse();
}
