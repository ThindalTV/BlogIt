using BlogIt.MauiAdmin.Core.Navigation;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Guards the phone client's navigation, which was entirely broken: MorePage navigated to
/// <c>//ai</c>, <c>//settings</c>, <c>//sites</c> and the rest, but absolute Shell routes only
/// resolve within the current Shell's own hierarchy, and PhoneShell's TabBar contains none of
/// those destinations. Every item on the More tab threw at runtime, which meant a phone user
/// could not reach site management at all — no way to add, switch or sign in to a second blog.
///
/// The Shells and the More menu are XAML and MAUI-typed code, so they are asserted against
/// their source text here. That is coarse, but it is the level at which this bug lived: the
/// catalog, the Shell and the menu each looked reasonable alone and only disagreed with each
/// other.
/// </summary>
public class MauiNavigationTests
{
    private static string MauiSource(params string[] parts) =>
        File.ReadAllText(RepoLayout.Combine([.. new[] { "src", "BlogIt.MauiAdmin" }, .. parts]));

    private static string AppRoutes => MauiSource("AppRoutes.cs");
    private static string PhoneShell => MauiSource("Views", "Navigation", "PhoneShell.xaml");
    private static string CompactShell => MauiSource("Views", "Navigation", "CompactShell.xaml");
    private static string DesktopShell => MauiSource("Views", "Navigation", "DesktopShell.xaml");
    private static string MorePage => MauiSource("Views", "More", "MorePage.xaml.cs");
    private static string MorePageMarkup => MauiSource("Views", "More", "MorePage.xaml");

    [Fact]
    public void EverySecondaryDestinationIsReachableFromThePhone()
    {
        var routes = AppRoutes;

        routes.Should().Contain("PhoneRoute",
            "the phone reaches the secondary destinations through registered routes, and those " +
            "registrations must be driven by the catalog rather than a hand-maintained list");

        foreach (var destination in AppNavigation.Secondary)
            routes.Should().Contain($"[\"{destination.Key}\"] = typeof({destination.PageTypeName})",
                $"{destination.PhoneRoute} must resolve to a page, or tapping {destination.Label} " +
                "on the More tab throws instead of navigating");
    }

    [Fact]
    public void MorePageNeverUsesAbsoluteRoutesThePhoneShellDoesNotDeclare()
    {
        var more = MorePage;
        var phoneShell = PhoneShell;

        foreach (var destination in AppNavigation.Secondary)
        {
            phoneShell.Should().NotContain($"Route=\"{destination.Key}\"",
                $"{destination.Label} is not a phone tab — if it becomes one, this test and MorePage should change together");

            more.Should().NotContain($"\"{destination.ShellRoute}\"",
                $"{destination.ShellRoute} cannot resolve on the phone Shell, which does not contain that route; " +
                $"MorePage must navigate the registered route {destination.PhoneRoute} instead");

        }

        more.Should().Contain("RouteTo",
            "MorePage must resolve routes through DestinationRouter rather than hard-coding them, " +
            "so the phone and flyout layouts cannot drift apart again");
    }

    [Fact]
    public void PhoneTabBarCarriesEveryPrimaryDestinationPlusMore()
    {
        var phoneShell = PhoneShell;

        foreach (var destination in AppNavigation.Primary)
            phoneShell.Should().Contain($"Route=\"{destination.Key}\"",
                $"{destination.Label} is a daily-operations screen and must stay one tap away on a phone");

        phoneShell.Should().Contain($"Route=\"{AppNavigation.MoreTabRoute}\"",
            "the More tab is what makes the secondary destinations reachable on a phone");
    }

    [Fact]
    public void FlyoutShellsDeclareEveryDestinationAsTopLevel()
    {
        foreach (var shell in new[] { CompactShell, DesktopShell })
            foreach (var destination in AppNavigation.All)
                shell.Should().Contain($"Route=\"{destination.Key}\"",
                    $"the flyout shells list {destination.Label} directly rather than behind a More tab");
    }

    [Fact]
    public void PhoneRoutesSecondaryDestinationsThroughTheMoreTabAndPrimariesDirectly()
    {
        var phone = new DestinationRouter(ShellLayout.Phone);

        phone.RouteTo("sites").Should().Be("more/sites",
            "Sites is not a phone tab, so it must be pushed as a registered route");
        phone.RouteTo("settings").Should().Be("more/settings");
        phone.RouteTo("dashboard").Should().Be("//dashboard",
            "the primary destinations are real tabs on the phone Shell, so absolute routing works");
    }

    [Fact]
    public void FlyoutLayoutsRouteEveryDestinationAbsolutely()
    {
        var flyout = new DestinationRouter(ShellLayout.Flyout);

        foreach (var destination in AppNavigation.All)
            flyout.RouteTo(destination.Key).Should().Be($"//{destination.Key}",
                "every destination is a top-level flyout item outside the phone layout");
    }

    [Fact]
    public void RoutingToAnUnknownDestinationFailsLoudly()
    {
        var act = () => new DestinationRouter(ShellLayout.Phone).RouteTo("nonsense");

        act.Should().Throw<ArgumentOutOfRangeException>(
            "a typo in a destination key should fail at the call site, not navigate nowhere");
    }

    [Fact]
    public void SiteSwitchingIsReachableOnThePhone()
    {
        MorePageMarkup.Should().Contain("SiteSwitcherView",
            "the phone Shell has no flyout, so the More tab is where switching between blogs has to live");

        MauiSource("ViewModels", "Sites", "SiteSwitcherViewModel.cs").Should().NotContain("\"//sites\"",
            "Manage Sites must route through DestinationRouter — //sites throws on the phone Shell");
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
