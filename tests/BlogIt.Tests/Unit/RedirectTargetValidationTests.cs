using BlogIt.Api;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers what a redirect may point at: local paths, external HTTP(S) URLs, and nothing else.
/// </summary>
/// <remarks>
/// Split out from the source-prefix tests because this half had a platform-dependent bug that
/// passed every test on Windows and broke the feature on Linux. <c>Uri.TryCreate</c> with
/// <see cref="UriKind.Absolute"/> parses a rooted path such as <c>/destination</c> as a
/// <c>file://</c> URI on Unix — but not on Windows — so the validator saw a non-HTTP scheme and
/// rejected every internal redirect with "External targets must use HTTP or HTTPS". Local
/// redirects are the feature's main use, so the feature was effectively dead on the platform most
/// deployments use.
/// <para>
/// These assertions are all platform-independent by construction: each one pins a decision that
/// must not depend on where the tests happen to run.
/// </para>
/// </remarks>
public class RedirectTargetValidationTests
{
    [Theory]
    [InlineData("/destination")]
    [InlineData("/blog/2019/new-home")]
    [InlineData("/a")]
    public void ARootedTarget_IsALocalPath_OnEveryPlatform(string target)
    {
        TryNormalize("/source", target, out var normalized, out var error)
            .Should().BeTrue(error);
        normalized.Should().Be(target);
    }

    [Theory]
    [InlineData("https://example.com/moved")]
    [InlineData("http://example.com/moved")]
    public void AnAbsoluteHttpTarget_IsAccepted(string target)
    {
        TryNormalize("/source", target, out var normalized, out var error)
            .Should().BeTrue(error);
        normalized.Should().Be(target);
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    public void AnAbsoluteTargetWithAnotherScheme_IsRejected(string target)
    {
        TryNormalize("/source", target, out _, out var error).Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Theory]
    // Protocol-relative: rooted, so it reaches the local branch, which has to keep refusing it —
    // "//evil.example/x" is an off-site jump wearing the shape of a local path.
    [InlineData("//evil.example/takeover")]
    [InlineData("/bad\\path")]
    public void ATargetThatOnlyLooksLocal_IsRejected(string target)
    {
        TryNormalize("/source", target, out _, out var error).Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    [Fact]
    public void AnUnrootedRelativeTarget_IsNormalizedToALocalPath()
    {
        TryNormalize("/source", "destination", out var normalized, out var error)
            .Should().BeTrue(error);
        normalized.Should().Be("/destination");
    }

    private static bool TryNormalize(
        string source,
        string target,
        out string targetUrl,
        out string error) =>
        RedirectPathValidator.TryNormalize(
            source, target, new BlogItOptions(), out _, out targetUrl, out error);
}
