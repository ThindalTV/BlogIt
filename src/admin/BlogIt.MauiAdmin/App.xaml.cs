using BlogIt.MauiAdmin.Core.Navigation;
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
        // site's own login screen rather than a generic error. In the window they are working
        // in — Shell.Current would always pick the first window opened. Absolute, because the 401
        // usually comes from outside the Sites section, where the relative "sites/login" makes
        // Shell read "sites" as the FlyoutItem and throw.
        WeakReferenceMessenger.Default.Register<SiteAuthExpiredMessage>(this, async (_, message) =>
        {
            var windows = ServiceHelper.GetRequiredService<ActiveWindowTracker>();
            if (windows.CurrentWindow?.Page is Microsoft.Maui.Controls.Shell shell)
                await shell.GoToAsync($"{AppNavigation.RouteTo("sites")}/login?id={message.SiteId}");
        });
    }

    /// <remarks>
    /// One Shell for every platform and every window size. The layout it presents is its own
    /// business, re-decided from its width as that changes — see <see cref="AppShell"/>. Nothing
    /// here inspects the device, and no window size is imposed: the window used to be clamped to a
    /// 1000-unit minimum so it could not reach a layout the app had no answer for, which is a
    /// constraint the adaptive Shell removes the need for.
    /// </remarks>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(ServiceHelper.GetRequiredService<AppShell>()) { Title = "BlogIt Admin" };
        ServiceHelper.GetRequiredService<ActiveWindowTracker>().Track(window);
        return window;
    }
}
