using BlogIt.MauiAdmin.Services;
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

        builder.Services.AddMauiBlazorWebView();

        // BlogIt services
        builder.Services.AddSingleton<SiteProfileService>();
        builder.Services.AddScoped<MauiApiClient>();
        builder.Services.AddScoped<MediaCaptureService>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

