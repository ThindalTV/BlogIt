using BlogIt.Shared;
using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

public class SlugHelperTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("  Trim Me  ", "trim-me")]
    [InlineData("C# .NET Rocks!", "c-net-rocks")]
    [InlineData("Café au lait", "cafe-au-lait")]
    [InlineData("multiple---hyphens", "multiple-hyphens")]
    [InlineData("UPPERCASE", "uppercase")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Slugify_ProducesExpectedSlug(string input, string expected)
    {
        SlugHelper.Slugify(input).Should().Be(expected);
    }

    [Fact]
    public void Slugify_UnderscoreBecomesHyphen()
    {
        SlugHelper.Slugify("hello_world").Should().Be("hello-world");
    }

    [Theory]
    [InlineData("Привет мир")]
    [InlineData("日本語のタイトル")]
    [InlineData("!!!")]
    [InlineData("")]
    [InlineData("   ")]
    public void Slugify_KeepsReturningEmpty_ForInputWithNothingSlugAble(string input)
    {
        // Deliberately unchanged. TagResolver relies on the empty result to drop a tag name it
        // cannot address, and both update paths compare the slugified request against the stored
        // slug — a fallback here would change both.
        SlugHelper.Slugify(input).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Привет мир", "830d1964")]
    [InlineData("日本語のタイトル", "348774f3")]
    [InlineData("!!!", "e84c538e")]
    [InlineData("", "e3b0c442")]
    public void SlugifyOrFallback_ProducesAStableTokenForNonLatinInput(string input, string expected)
    {
        // The literals are pinned on purpose: they are the whole point of the fallback. A slug that
        // moved between the preview and the publish of the same draft would be its own bug, so a
        // per-process hash such as string.GetHashCode() is not usable here and pinning the values
        // is what catches a switch to one.
        SlugHelper.SlugifyOrFallback(input).Should().Be(expected);
    }

    [Fact]
    public void SlugifyOrFallback_IgnoresSurroundingWhitespace()
    {
        SlugHelper.SlugifyOrFallback("  Привет мир  ")
            .Should().Be(SlugHelper.SlugifyOrFallback("Привет мир"));
    }

    [Fact]
    public void SlugifyOrFallback_GivesDifferentTitlesDifferentSlugs()
    {
        SlugHelper.SlugifyOrFallback("Привет мир")
            .Should().NotBe(SlugHelper.SlugifyOrFallback("Прощай мир"));
    }

    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("Café au lait", "cafe-au-lait")]
    public void SlugifyOrFallback_LeavesASlugAbleTitleAlone(string input, string expected)
    {
        SlugHelper.SlugifyOrFallback(input).Should().Be(expected);
    }

    [Fact]
    public void SlugifyOrFallback_ProducesOnlySlugCharacters()
    {
        SlugHelper.SlugifyOrFallback("¡¿€₽!").Should().MatchRegex("^[a-z0-9-]+$");
    }

    [Fact]
    public void EnsureUnique_ReturnsBaseSlug_WhenNoConflict()
    {
        SlugHelper.EnsureUnique("foo", ["bar", "baz"], ContentLimits.SlugLength).Should().Be("foo");
    }

    [Fact]
    public void EnsureUnique_AppendsCounter_WhenConflictExists()
    {
        SlugHelper.EnsureUnique("foo", ["foo"], ContentLimits.SlugLength).Should().Be("foo-2");
    }

    [Fact]
    public void EnsureUnique_SkipsExistingCounters()
    {
        SlugHelper.EnsureUnique("foo", ["foo", "foo-2", "foo-3"], ContentLimits.SlugLength)
            .Should().Be("foo-4");
    }

    [Fact]
    public void EnsureUnique_IsCaseInsensitive()
    {
        SlugHelper.EnsureUnique("Foo", ["FOO"], ContentLimits.SlugLength).Should().Be("Foo-2");
    }

    [Fact]
    public void EnsureUnique_TruncatesABaseSlugThatAlreadyFillsTheColumn()
    {
        var atTheLimit = new string('a', ContentLimits.SlugLength);

        SlugHelper.EnsureUnique(atTheLimit, [], ContentLimits.SlugLength)
            .Should().Be(atTheLimit);
    }

    [Fact]
    public void EnsureUnique_KeepsTheSuffixedSlugInsideTheColumn()
    {
        // Finding #46: Title and Slug are both 500 wide, so a maximum-length title collides with
        // itself and the "-2" pushed the value to 502 against a 500-wide column.
        var atTheLimit = new string('a', ContentLimits.SlugLength);

        var second = SlugHelper.EnsureUnique(atTheLimit, [atTheLimit], ContentLimits.SlugLength);

        second.Length.Should().BeLessThanOrEqualTo(ContentLimits.SlugLength);
        second.Should().NotBe(atTheLimit);
        second.Should().EndWith("-2");
    }

    [Fact]
    public void EnsureUnique_KeepsCountingWithinTheColumn_WhenSeveralMaximumLengthSlugsCollide()
    {
        var atTheLimit = new string('a', ContentLimits.SlugLength);
        var taken = new List<string> { atTheLimit };

        for (var i = 0; i < 12; i++)
        {
            var next = SlugHelper.EnsureUnique(atTheLimit, taken, ContentLimits.SlugLength);
            next.Length.Should().BeLessThanOrEqualTo(ContentLimits.SlugLength);
            taken.Should().NotContain(next);
            taken.Add(next);
        }
    }

    [Fact]
    public void EnsureUnique_ShortensAnOverLongBaseSlugToFit()
    {
        var overLong = new string('a', ContentLimits.SlugLength + 50);

        SlugHelper.EnsureUnique(overLong, [], ContentLimits.SlugLength)
            .Length.Should().Be(ContentLimits.SlugLength);
    }

    [Fact]
    public void CollisionKeys_NarrowToTheCandidatesEnsureUniqueCanProduce()
    {
        // The pair is what turns "load every slug in the table" into an index-seekable lookup.
        var (exact, prefix) = SlugHelper.CollisionKeys("hello", ContentLimits.SlugLength);

        exact.Should().Be("hello");
        prefix.Should().Be("hello-");
    }

    [Fact]
    public void CollisionKeys_CoverBothStems_WhenTheBaseSlugFillsTheColumn()
    {
        // A truncated base and its suffixed candidates do not share one prefix, so the exact key
        // carries the untruncated form and the prefix key carries the shortened stem.
        var atTheLimit = new string('a', ContentLimits.SlugLength);

        var (exact, prefix) = SlugHelper.CollisionKeys(atTheLimit, ContentLimits.SlugLength);
        var second = SlugHelper.EnsureUnique(atTheLimit, [atTheLimit], ContentLimits.SlugLength);

        exact.Should().Be(atTheLimit);
        prefix.Length.Should().BeLessThan(atTheLimit.Length);
        second.Should().StartWith(prefix);
    }
}
