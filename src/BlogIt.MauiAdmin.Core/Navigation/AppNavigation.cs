namespace BlogIt.MauiAdmin.Core.Navigation;

/// <summary>
/// The single source of truth for what the admin client can navigate to. The three Shells and
/// the More menu are all expected to agree with this catalog, and
/// <c>BlogIt.Tests.Unit.MauiNavigationTests</c> asserts that they do.
/// </summary>
public static class AppNavigation
{
    /// <summary>
    /// Destinations every layout shows as a first-class item — the phone tab bar's first four
    /// tabs, and the top of the flyout everywhere else. These are the daily-operations screens.
    /// </summary>
    public static IReadOnlyList<AppDestination> Primary { get; } =
    [
        new("dashboard", "Dashboard", "DashboardPage"),
        new("posts", "Posts", "PostListPage"),
        new("pages", "Pages", "PageListPage"),
        new("media", "Media", "MediaListPage"),
    ];

    /// <summary>
    /// Destinations the flyout shells list directly, and the phone reaches through its More tab
    /// because a ten-item bottom bar is not viable.
    /// </summary>
    public static IReadOnlyList<AppDestination> Secondary { get; } =
    [
        new("ai", "AI", "ConversationListPage"),
        new("redirects", "Redirects", "RedirectListPage"),
        new("users", "Users", "UserListPage"),
        new("settings", "Settings", "SettingsPage"),
        new("account", "My Account", "AccountPage"),
        new("sites", "Sites", "SiteListPage"),
    ];

    public static IEnumerable<AppDestination> All => [.. Primary, .. Secondary];

    /// <summary>The phone tab bar: the four primary destinations plus the More tab itself.</summary>
    public const string MoreTabRoute = "more";

    /// <summary>
    /// Detail pages pushed onto a navigation stack from a list, rather than selected from a menu.
    /// Registered globally, so they resolve the same way in every Shell.
    /// </summary>
    public static IReadOnlyList<string> DetailRoutes { get; } =
    [
        "posts/edit",
        "posts/new",
        "pages/edit",
        "pages/new",
        "ai/chat",
        "sites/add",
        "sites/login",
        "sites/setup-required",
    ];

    /// <summary>
    /// Every route that must be registered with <c>Routing.RegisterRoute</c>: the detail pages,
    /// plus the phone-only route for each secondary destination.
    /// </summary>
    public static IEnumerable<string> RoutesRequiringRegistration =>
        [.. DetailRoutes, .. Secondary.Select(d => d.PhoneRoute)];

    public static AppDestination Find(string key) =>
        All.FirstOrDefault(d => d.Key == key)
        ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown navigation destination.");
}
