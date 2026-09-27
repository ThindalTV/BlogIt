namespace BlogIt.MauiAdmin.Core.Navigation;

/// <summary>
/// The single source of truth for what the admin client can navigate to. <c>AppShell.xaml</c> is
/// expected to agree with this catalog, and <c>BlogIt.Tests.Unit.MauiNavigationTests</c> asserts
/// that it does.
/// </summary>
public static class AppNavigation
{
    /// <summary>
    /// The daily-operations screens. Listed first in the navigation menu at every window size.
    /// </summary>
    public static IReadOnlyList<AppDestination> Primary { get; } =
    [
        new("dashboard", "Dashboard", "DashboardPage"),
        new("posts", "Posts", "PostListPage"),
        new("pages", "Pages", "PageListPage"),
        new("media", "Media", "MediaListPage"),
    ];

    /// <summary>
    /// Everything else: reached from the same menu, below a separator.
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

    /// <summary>
    /// Detail pages pushed onto a navigation stack from a list, rather than selected from the
    /// menu. These are the only routes that need registering with <c>Routing.RegisterRoute</c> —
    /// every destination in the catalog is declared in the Shell itself.
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

    public static AppDestination Find(string key) =>
        All.FirstOrDefault(d => d.Key == key)
        ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown navigation destination.");

    /// <summary>The absolute route to <paramref name="key"/>, validated against the catalog.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is not a known destination.</exception>
    public static string RouteTo(string key) => Find(key).ShellRoute;
}
