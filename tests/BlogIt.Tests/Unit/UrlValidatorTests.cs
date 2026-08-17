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
}
