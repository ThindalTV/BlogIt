using BlogIt.MauiAdmin.Views.Ai;
using BlogIt.MauiAdmin.Views.Pages;
using BlogIt.MauiAdmin.Views.Posts;
using BlogIt.MauiAdmin.Views.Sites;

namespace BlogIt.MauiAdmin;

/// <summary>Registers the detail pages, which are pushed onto a navigation stack from a list
/// rather than selected from the menu. Every destination in <c>AppNavigation</c> is declared in
/// AppShell.xaml instead and needs nothing here.</summary>
/// <remarks>
/// This file used to carry a second table mapping each secondary destination to a phone-only
/// route, because those destinations were absent from the phone Shell's hierarchy and
/// <c>//settings</c> could not resolve there. With one Shell that declares everything, that table
/// has no reason to exist — see <c>AppDestination.ShellRoute</c>.
/// </remarks>
public static class AppRoutes
{
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
    }
}
