using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.ViewModels.Account;
using BlogIt.MauiAdmin.ViewModels.Ai;
using BlogIt.MauiAdmin.ViewModels.Dashboard;
using BlogIt.MauiAdmin.ViewModels.Media;
using BlogIt.MauiAdmin.ViewModels.Pages;
using BlogIt.MauiAdmin.ViewModels.Posts;
using BlogIt.MauiAdmin.ViewModels.Redirects;
using BlogIt.MauiAdmin.ViewModels.Settings;
using BlogIt.MauiAdmin.ViewModels.Sites;
using BlogIt.MauiAdmin.ViewModels.Users;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace BlogIt.MauiAdmin;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        AppRoutes.RegisterAll();

        // ── Cross-cutting services ─────────────────────────────────────
        // Pages are never resolved through this container directly (Shell's XAML
        // {DataTemplate} and route factory both construct pages via
        // Activator.CreateInstance) — only ViewModels are registered here, and each
        // page's parameterless constructor pulls its ViewModel via ServiceHelper.
        builder.Services.AddSingleton<SiteProfileService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddTransient<ActiveSiteHttpMessageHandler>();
        builder.Services.AddHttpClient("BlogIt").AddHttpMessageHandler<ActiveSiteHttpMessageHandler>();
        builder.Services.AddSingleton<MauiApiClient>();
        builder.Services.AddSingleton<SiteProbeService>();
        builder.Services.AddSingleton<IMediaCaptureService, MediaCaptureService>();

        // ── Sites (Phase 1) ─────────────────────────────────────────────
        builder.Services.AddTransient<SiteListViewModel>();
        builder.Services.AddTransient<AddSiteViewModel>();
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<SetupRequiredViewModel>();
        builder.Services.AddSingleton<SiteSwitcherViewModel>();

        // ── Dashboard / Posts / Pages (Phase 2) ─────────────────────────
        builder.Services.AddTransient<DashboardViewModel>();
        builder.Services.AddTransient<PostListViewModel>();
        builder.Services.AddTransient<PostEditViewModel>();
        builder.Services.AddTransient<PageListViewModel>();
        builder.Services.AddTransient<PageEditViewModel>();

        // ── Media (Phase 3) ─────────────────────────────────────────────
        builder.Services.AddTransient<MediaListViewModel>();
        builder.Services.AddTransient<MediaPickerViewModel>();

        // ── AI (Phase 4) ────────────────────────────────────────────────
        builder.Services.AddTransient<ConversationListViewModel>();
        builder.Services.AddTransient<ConversationChatViewModel>();

        // ── Users / Settings / Redirects / Account (Phase 5) ────────────
        builder.Services.AddTransient<UserListViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<RedirectListViewModel>();
        builder.Services.AddTransient<AccountViewModel>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
