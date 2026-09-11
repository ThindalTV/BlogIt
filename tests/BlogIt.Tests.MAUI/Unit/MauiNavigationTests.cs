using BlogIt.MauiAdmin.Core.Navigation;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Guards the client's navigation, which has been broken in this codebase twice over.
///
/// First by disagreement: there were three Shells, and MorePage navigated to <c>//ai</c>,
/// <c>//settings</c>, <c>//sites</c> and the rest, but absolute Shell routes only resolve within
/// the current Shell's own hierarchy and the phone Shell's TabBar contained none of those
/// destinations. Every item on the More tab threw at runtime, so a phone user could not reach site
/// management at all — no way to add, switch or sign in to a second blog.
///
/// Then by rigidity: which of the three Shells you got was decided once per process from
/// DeviceInfo, so a foldable opened mid-session kept the layout of a shut phone, and no window
/// resize changed anything.
///
/// Both are answered by one Shell that declares every destination and re-decides only its
/// FlyoutBehavior as the window changes width. These tests hold that shape in place. The Shell is
/// XAML and its layout code is MAUI-typed, so they are asserted against source text — coarse, but
/// it is the level at which both bugs lived: the catalog, the Shell and the menu each looked
/// reasonable alone and only disagreed with each other.
/// </summary>
public class MauiNavigationTests
{
    private static string MauiSource(params string[] parts) =>
        File.ReadAllText(RepoLayout.Combine([.. new[] { "src", "BlogIt.MauiAdmin" }, .. parts]));

    private static string AppRoutes => MauiSource("AppRoutes.cs");
    private static string AppShellMarkup => MauiSource("Views", "Navigation", "AppShell.xaml");
    private static string AppShellCode => MauiSource("Views", "Navigation", "AppShell.xaml.cs");

    private static string NavigationDirectory =>
        RepoLayout.Combine("src", "BlogIt.MauiAdmin", "Views", "Navigation");

    [Fact]
    public void ThereIsExactlyOneShell()
    {
        var shells = Directory.GetFiles(NavigationDirectory, "*Shell.xaml")
            .Select(Path.GetFileName)
            .ToArray();

        shells.Should().BeEquivalentTo(["AppShell.xaml"],
            "a second Shell means a layout the app can only reach by being restarted into it — " +
            "swapping Window.Page mid-session tears down the navigation stack and every live page, " +
            "which is exactly the half-written post an unfolding device must not lose");
    }

    [Fact]
    public void EveryDestinationIsATopLevelItemInTheShell()
    {
        var shell = AppShellMarkup;

        foreach (var destination in AppNavigation.All)
        {
            shell.Should().Contain($"Route=\"{destination.Key}\"",
                $"{destination.Label} must be declared in the Shell, or //{destination.Key} cannot resolve");

            // Matches "{DataTemplate dashboard:DashboardPage}" without pinning the xmlns prefix.
            shell.Should().Contain($":{destination.PageTypeName}}}",
                $"{destination.Label}'s route must resolve to a page — a route with nothing behind it " +
                "throws on navigation rather than going missing quietly");
        }
    }

    [Fact]
    public void NoDestinationCarriesALayoutSpecificRoute()
    {
        foreach (var destination in AppNavigation.All)
            AppNavigation.RouteTo(destination.Key).Should().Be($"//{destination.Key}",
                "one Shell declaring everything means one route per destination; a second, " +
                "layout-specific route is what left the phone's More menu dead");

        AppShellMarkup.Should().NotContain("more/",
            "the More tab existed only because the phone Shell could not hold every destination");
        AppRoutes.Should().NotContain("more/",
            "no destination needs registering under a phone-only route any more");
    }

    [Fact]
    public void RoutingToAnUnknownDestinationFailsLoudly()
    {
        var act = () => AppNavigation.RouteTo("nonsense");

        act.Should().Throw<ArgumentOutOfRangeException>(
            "a typo in a destination key should fail at the call site, not navigate nowhere");
    }

    [Fact]
    public void TheShellTakesItsLayoutFromTheWindowWidthRatherThanAConstant()
    {
        AppShellMarkup.Should().NotContain("FlyoutBehavior=",
            "a FlyoutBehavior fixed in markup cannot respond to a fold; AppShell.xaml.cs sets it");

        var code = AppShellCode;
        code.Should().Contain("FlyoutBehavior",
            "changing FlyoutBehavior on the live Shell is what re-lays-out the app without rebuilding it");
        code.Should().Contain("ModeChanged",
            "the Shell must react to layout changes, not only read the mode once at construction");
        code.Should().Contain("SizeChanged",
            "something has to notice the window changed width — that is the signal a fold produces");
    }

    [Fact]
    public void SiteSwitchingIsReachableAtEveryWindowSize()
    {
        AppShellMarkup.Should().Contain("SiteSwitcherView",
            "the switcher lives in the one Shell's flyout header so every platform has it — it used " +
            "to be declared only in the desktop Shell, leaving tablets and macOS without it");

        MauiSource("ViewModels", "Sites", "SiteSwitcherViewModel.cs").Should().Contain("ActivateAsync",
            "switching must go through the shared activation path, or a site whose session has " +
            "expired lands on a dashboard that only 401s");
    }

    [Fact]
    public void EveryDetailRouteIsRegistered()
    {
        var routes = AppRoutes;

        foreach (var route in AppNavigation.DetailRoutes)
            routes.Should().Contain($"\"{route}\"",
                $"{route} is pushed by relative navigation and throws unless registered");
    }
}
