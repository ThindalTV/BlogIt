using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// BlogIt's convention for optional text: <c>null</c> means the value does not exist, an empty
/// string means it exists and is empty. Both halves matter — writers normalise "the author left this
/// blank" to null, and readers still have to cope with the empty strings already in the database
/// from before that rule existed.
/// </summary>
public class OptionalTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void OrNull_TreatsAnUnfilledFieldAsNonExistent(string? blank) =>
        OptionalText.OrNull(blank).Should().BeNull();

    [Fact]
    public void OrNull_LeavesRealTextAlone() =>
        OptionalText.OrNull(" A title ").Should().Be(" A title ");

    [Fact]
    public void FirstPresent_SkipsEmptyStringsWhereNullCoalescingWouldNot()
    {
        // The whole point. `"" ?? fallback` is "", which is how every post saved without SEO fields
        // rendered an empty <title> and an empty og:title.
        OptionalText.FirstPresent("", "Post title").Should().Be("Post title");
        OptionalText.FirstPresent("   ", "Post title").Should().Be("Post title");
    }

    [Fact]
    public void FirstPresent_SkipsNullsToo() =>
        OptionalText.FirstPresent(null, null, "Site name").Should().Be("Site name");

    [Fact]
    public void FirstPresent_PrefersTheEarliestCandidateThatCarriesText() =>
        OptionalText.FirstPresent("SEO title", "Post title").Should().Be("SEO title");

    [Fact]
    public void FirstPresent_IsNullWhenNothingCarriesText() =>
        OptionalText.FirstPresent(null, "", "  ").Should().BeNull();
}
