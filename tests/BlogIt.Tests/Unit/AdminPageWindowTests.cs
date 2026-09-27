using BlogIt.Admin.Services;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Page-button arithmetic behind the admin's shared Pager. Extracted from the component so the
/// awkward cases — a partial last page, a current page pinned to either end, a total that outgrows
/// the button strip — are covered without rendering anything.
/// </summary>
public class AdminPageWindowTests
{
    [Theory]
    [InlineData(0, 20, 0)]
    [InlineData(1, 20, 1)]
    [InlineData(20, 20, 1)]
    [InlineData(21, 20, 2)]
    [InlineData(200, 20, 10)]
    [InlineData(201, 20, 11)]
    public void TotalPages_CountsThePartialLastPage(int totalCount, int pageSize, int expected)
        => AdminPageWindow.TotalPages(totalCount, pageSize).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void TotalPages_TreatsANonsensePageSizeAsOne(int pageSize)
        => AdminPageWindow.TotalPages(7, pageSize).Should().Be(7);

    [Fact]
    public void Build_ShowsEveryPageWhenTheyFit()
        => AdminPageWindow.Build(currentPage: 2, totalPages: 3).Should().Equal(1, 2, 3);

    [Fact]
    public void Build_KeepsTheWindowAtTheStartWhileTheCurrentPageIsNearIt()
        => AdminPageWindow.Build(currentPage: 3, totalPages: 20).Should().Equal(1, 2, 3, 4, 5, 6, 7);

    [Fact]
    public void Build_CentresTheWindowOnTheCurrentPage()
        => AdminPageWindow.Build(currentPage: 10, totalPages: 20).Should().Equal(7, 8, 9, 10, 11, 12, 13);

    [Fact]
    public void Build_PinsTheWindowToTheEndOnTheLastPages()
        => AdminPageWindow.Build(currentPage: 20, totalPages: 20).Should().Equal(14, 15, 16, 17, 18, 19, 20);

    [Fact]
    public void Build_ReturnsNothingWhenThereIsNoData()
        => AdminPageWindow.Build(currentPage: 1, totalPages: 0).Should().BeEmpty();

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(999)]
    public void Build_ClampsAnOutOfRangeCurrentPageIntoTheWindow(int currentPage)
    {
        var window = AdminPageWindow.Build(currentPage, totalPages: 20);

        window.Should().HaveCount(7);
        window.Should().BeInAscendingOrder();
        window.Should().OnlyContain(p => p >= 1 && p <= 20);
    }

    [Theory]
    [InlineData(0, 5, 1)]
    [InlineData(3, 5, 3)]
    [InlineData(9, 5, 5)]
    [InlineData(2, 0, 1)]
    public void Clamp_KeepsAPageRequestInsideTheAvailableRange(int page, int totalPages, int expected)
        => AdminPageWindow.Clamp(page, totalPages).Should().Be(expected);
}
