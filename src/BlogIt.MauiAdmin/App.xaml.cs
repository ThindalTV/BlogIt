using BlogIt.MauiAdmin.Messages;
using BlogIt.MauiAdmin.Services;
using BlogIt.MauiAdmin.Views.Navigation;
using CommunityToolkit.Mvvm.Messaging;

namespace BlogIt.MauiAdmin;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Central reaction to a 401 from any site: send the user back to that
        // site's own login screen rather than a generic error.
        WeakReferenceMessenger.Default.Register<SiteAuthExpiredMessage>(this, async (_, message) =>
        {
            if (Microsoft.Maui.Controls.Shell.Current is not null)
                await Microsoft.Maui.Controls.Shell.Current.GoToAsync($"sites/login?id={message.SiteId}");
        });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Microsoft.Maui.Controls.Shell shell = AppLayout.Mode switch
        {
            LayoutMode.DesktopWide => new DesktopShell(),
            LayoutMode.Compact => new CompactShell(),
            _ => new PhoneShell(),
        };

        var window = new Window(shell) { Title = "BlogIt Admin" };

        if (AppLayout.Mode == LayoutMode.DesktopWide)
        {
            // Enforce "wide, non-responsive" — a persistent nav rail, not a
            // reflowing phone-style layout that could get squeezed into a broken
            // in-between state.
            window.Width = 1280;
            window.Height = 800;
            window.MinimumWidth = 1000;
            window.MinimumHeight = 700;
        }

        return window;
    }
}
