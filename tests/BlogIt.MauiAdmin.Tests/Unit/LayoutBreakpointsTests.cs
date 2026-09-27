using BlogIt.MauiAdmin.Core.Navigation;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The width-to-layout decision, which is the whole of what makes the client adapt without a
/// restart. The cases that matter are the two states of a foldable: the layout used to be chosen
/// once per process from DeviceInfo.Idiom, and a Galaxy Z Fold reports the phone idiom whether it
/// is shut or open — so an unfolded device got the layout of a shut one until the app was killed
/// and reopened.
/// </summary>
public class LayoutBreakpointsTests
{
    // Approximate device-independent widths, which is what MAUI reports.
    private const double FoldClosed = 344;   // Galaxy Z Fold cover display
    private const double FoldOpen = 690;     // Galaxy Z Fold inner display
    private const double Phone = 393;        // iPhone 15 portrait
    private const double TabletPortrait = 820;
    private const double TabletLandscape = 1180;
    private const double DesktopWindow = 1280;

    [Fact]
    public void AFoldableChangesLayoutWhenItOpens()
    {
        LayoutBreakpoints.ModeFor(FoldClosed).Should().Be(LayoutMode.Narrow);
        LayoutBreakpoints.ModeFor(FoldOpen).Should().Be(LayoutMode.Medium,
            "opening the device must reach a different layout, or unfolding changes nothing at all");
    }

    [Theory]
    [InlineData(Phone)]
    [InlineData(FoldClosed)]
    public void PhoneSizedWindowsKeepTheMenuCollapsed(double width) =>
        LayoutBreakpoints.ModeFor(width).Should().Be(LayoutMode.Narrow);

    [Theory]
    [InlineData(TabletLandscape)]
    [InlineData(DesktopWindow)]
    public void WideWindowsGetTheRail(double width) =>
        LayoutBreakpoints.ModeFor(width).Should().Be(LayoutMode.Wide);

    [Fact]
    public void ATabletTurnedOnItsSideChangesLayout()
    {
        LayoutBreakpoints.ModeFor(TabletPortrait).Should().Be(LayoutMode.Medium);
        LayoutBreakpoints.ModeFor(TabletLandscape).Should().Be(LayoutMode.Wide);
    }

    [Fact]
    public void ADesktopWindowDraggedNarrowReflowsLikeATablet()
    {
        LayoutBreakpoints.ModeFor(DesktopWindow).Should().Be(LayoutMode.Wide);
        LayoutBreakpoints.ModeFor(700).Should().Be(LayoutMode.Medium,
            "the desktop window used to be clamped to a 1000-unit minimum precisely because the app " +
            "had no answer for being narrower; it has one now");
    }

    [Theory]
    [InlineData(LayoutBreakpoints.MediumMinimumWidth, LayoutMode.Medium)]
    [InlineData(LayoutBreakpoints.MediumMinimumWidth - 1, LayoutMode.Narrow)]
    [InlineData(LayoutBreakpoints.WideMinimumWidth, LayoutMode.Wide)]
    [InlineData(LayoutBreakpoints.WideMinimumWidth - 1, LayoutMode.Medium)]
    public void TheThresholdsThemselvesBelongToTheWiderLayout(double width, LayoutMode expected) =>
        LayoutBreakpoints.ModeFor(width).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AnUnmeasuredWindowCollapsesRatherThanThrowing(double width) =>
        LayoutBreakpoints.ModeFor(width).Should().Be(LayoutMode.Narrow,
            "every platform reports a width before first layout; expanding on the first real " +
            "measurement is invisible, tearing away a rail that was already drawn is not");
}
