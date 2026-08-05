using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

public class MarkdownHelperTests
{
    [Fact]
    public void ToHtml_RendersHeading()
    {
        var result = MarkdownHelper.ToHtml("# Hello");
        result.Should().Contain("<h1");
        result.Should().Contain("Hello");
    }

    [Fact]
    public void ToHtml_RendersBold()
    {
        MarkdownHelper.ToHtml("**bold**").Should().Contain("<strong>bold</strong>");
    }

    [Fact]
    public void ToHtml_PassesThroughHtml()
    {
        var html = "<div class=\"custom\">Hello</div>";
        MarkdownHelper.ToHtml(html).Should().Contain(html);
    }

    [Fact]
    public void ToHtml_ReturnsEmpty_ForNullOrWhitespace()
    {
        MarkdownHelper.ToHtml(null).Should().BeEmpty();
        MarkdownHelper.ToHtml("").Should().BeEmpty();
        MarkdownHelper.ToHtml("   ").Should().BeEmpty();
    }

    [Fact]
    public void ToPlainText_StripsTags()
    {
        var result = MarkdownHelper.ToPlainText("# Hello **world**");
        result.Should().NotContain("<");
        result.Should().Contain("Hello");
        result.Should().Contain("world");
    }

    [Fact]
    public void ToPlainText_ReturnsEmpty_ForNull()
    {
        MarkdownHelper.ToPlainText(null).Should().BeEmpty();
    }
}
