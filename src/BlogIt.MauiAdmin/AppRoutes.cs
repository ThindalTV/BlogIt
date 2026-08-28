using BlogIt.MauiAdmin.Core.Navigation;
using BlogIt.MauiAdmin.Views.Account;
using BlogIt.MauiAdmin.Views.Ai;
using BlogIt.MauiAdmin.Views.Media;
using BlogIt.MauiAdmin.Views.Pages;
using BlogIt.MauiAdmin.Views.Posts;
using BlogIt.MauiAdmin.Views.Redirects;
using BlogIt.MauiAdmin.Views.Settings;
using BlogIt.MauiAdmin.Views.Sites;
using BlogIt.MauiAdmin.Views.Users;

namespace BlogIt.MauiAdmin;

/// <summary>Registers every route reached by relative navigation. Flyout/tab items are
/// declared in the Shell XAML instead; only routes that are not part of the current Shell's
/// hierarchy need registering here.</summary>
public static class AppRoutes
{
    /// <summary>
    /// The page behind each secondary destination, keyed the same way as
    /// <see cref="AppNavigation.Secondary"/>. Kept as a lookup rather than inline
    /// <c>RegisterRoute</c> calls so the catalog stays the thing that decides what exists and
    /// this file only decides what renders it — a destination added to the catalog without a
    /// page here fails loudly at startup rather than silently going missing from the More tab.
    /// </summary>
    private static readonly Dictionary<string, Type> SecondaryPages = new()
    {
        ["ai"] = typeof(ConversationListPage),
        ["redirects"] = typeof(RedirectListPage),
        ["users"] = typeof(UserListPage),
        ["settings"] = typeof(SettingsPage),
        ["account"] = typeof(AccountPage),
        ["sites"] = typeof(SiteListPage),
    };

    public static void RegisterAll()
    {
        Routing.RegisterRoute("posts/edit", typeof(PostEditPage));
        Routing.RegisterRoute("posts/new", typeof(PostEditPage));
        Routing.RegisterRoute("pages/edit", typeof(PageEditPage));
        Routing.RegisterRoute("pages/new", typeof(PageEditPage));
        Routing.RegisterRoute("ai/chat", typeof(ConversationChatPage));
        Routing.RegisterRoute("sites/add", typeof(AddSitePage));
        Routing.RegisterRoute("sites/login", typeof(LoginPage));
        Routing.RegisterRoute("sites/setup-required", typeof(SetupRequiredPage));

        // The phone Shell's TabBar holds Dashboard/Posts/Pages/Media/More and nothing else, so
        // the secondary destinations are not in its hierarchy and //route navigation cannot
        // reach them there. Registering each one under its own phone route makes the More tab
        // work by pushing the page onto the current tab's stack, which also gives the user a
        // back button. The route is prefixed so it cannot collide with the same-named
        // FlyoutItem routes the compact and desktop Shells declare.
        foreach (var destination in AppNavigation.Secondary)
        {
            if (!SecondaryPages.TryGetValue(destination.Key, out var pageType))
                throw new InvalidOperationException(
                    $"No page is mapped for navigation destination '{destination.Key}'. " +
                    $"Add it to {nameof(AppRoutes)}.{nameof(SecondaryPages)}.");

            Routing.RegisterRoute(destination.PhoneRoute, pageType);
        }
    }
}
