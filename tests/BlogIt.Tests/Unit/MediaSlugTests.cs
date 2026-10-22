using BlogIt.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

public sealed class MediaSlugTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  / ", null)]
    [InlineData("CV.PDF", "cv.pdf")]
    [InlineData(" /Docs/My-File.pdf/ ", "docs/my-file.pdf")]
    public void Normalize_TrimsLowerCasesAndTreatsBlankAsNoSlug(string? input, string? expected) =>
        MediaSlug.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("cv.pdf")]
    [InlineData("docs/annual_report-2026.pdf")]
    [InlineData("a")]
    public void Validate_AcceptsPlainPaths(string slug) =>
        MediaSlug.Validate(slug).Should().BeNull();

    [Theory]
    [InlineData("a//b")]
    [InlineData("../secret")]
    [InlineData("a/./b")]
    [InlineData("has space.pdf")]
    [InlineData("q?x=1")]
    [InlineData("back\\slash")]
    [InlineData("Upper.pdf")]
    [InlineData("åäö.pdf")]
    public void Validate_RejectsUnsafeOrUnescapedPaths(string slug) =>
        MediaSlug.Validate(slug).Should().NotBeNull();

    [Fact]
    public void Validate_RejectsOverlongPaths() =>
        MediaSlug.Validate(new string('a', MediaSlug.MaxLength + 1)).Should().NotBeNull();
}
