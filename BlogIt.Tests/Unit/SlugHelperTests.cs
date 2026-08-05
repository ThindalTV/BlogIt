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

    [Fact]
    public void EnsureUnique_ReturnsBaseSlug_WhenNoConflict()
    {
        SlugHelper.EnsureUnique("foo", ["bar", "baz"]).Should().Be("foo");
    }

    [Fact]
    public void EnsureUnique_AppendsCounter_WhenConflictExists()
    {
        SlugHelper.EnsureUnique("foo", ["foo"]).Should().Be("foo-2");
    }

    [Fact]
    public void EnsureUnique_SkipsExistingCounters()
    {
        SlugHelper.EnsureUnique("foo", ["foo", "foo-2", "foo-3"]).Should().Be("foo-4");
    }

    [Fact]
    public void EnsureUnique_IsCaseInsensitive()
    {
        SlugHelper.EnsureUnique("Foo", ["FOO"]).Should().Be("Foo-2");
    }
}
