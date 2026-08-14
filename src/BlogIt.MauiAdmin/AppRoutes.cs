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

/// <summary>Registers every push/detail route once. Flyout/tab items are registered
/// declaratively in the Shell XAML; only routes navigated to via a relative path
/// (Shell.Current.GoToAsync) need an explicit registration here.</summary>
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
