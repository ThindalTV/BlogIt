using BlogIt.Admin.Shared;
using BlogIt.Tests.Helpers;
using Bunit;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// <see cref="Pager"/> is recent code sitting on three list screens, so it was checked for the same
/// defect class as the rest of the admin. It already used real buttons and already marked the current
/// page with <c>aria-current</c>; what it lacked was a name on the numbered buttons, where the
/// accessible name was a bare digit.
/// </summary>
public class AdminPagerMarkupTests
{
    private static IRenderedComponent<Pager> Render(int currentPage, int totalCount = 200)
    {
        var ctx = new AdminComponentHarness();
        return ctx.Render<Pager>(p => p
            .Add(x => x.CurrentPage, currentPage)
            .Add(x => x.TotalCount, totalCount)
            .Add(x => x.PageSize, 20));
    }

    [Fact]
    public void MarksExactlyTheCurrentPage()
    {
        var cut = Render(currentPage: 4);

        var current = cut.FindAll("[aria-current=page]");
        current.Should().HaveCount(1, "two current pages is as wrong as none");
        current[0].TextContent.Trim().Should().Be("4");
    }

    [Fact]
    public void EveryButtonHasAName()
    {
        var cut = Render(currentPage: 4);

        foreach (var button in cut.FindAll(".page-btn"))
        {
            button.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace(
                "“4” on its own does not say what pressing it does");
        }
    }

    [Fact]
    public void TheStripIsANamedNavigationLandmark()
    {
        var cut = Render(currentPage: 4);

        var nav = cut.Find("nav");
        nav.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TheEllipsisIsHiddenFromTheAccessibilityTree()
    {
        var cut = Render(currentPage: 10, totalCount: 600);

        var gaps = cut.FindAll(".page-gap");
        gaps.Should().NotBeEmpty("a 30-page total is wide enough to elide both ends");
        foreach (var gap in gaps)
            gap.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void RendersNothingForASinglePage()
    {
        var cut = Render(currentPage: 1, totalCount: 5);

        cut.Markup.Trim().Should().BeEmpty();
    }
}
