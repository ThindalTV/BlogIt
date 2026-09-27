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

    /// <remarks>
    /// One Shell for every platform and every window size. The layout it presents is its own
    /// business, re-decided from its width as that changes — see <see cref="AppShell"/>. Nothing
    /// here inspects the device, and no window size is imposed: the window used to be clamped to a
    /// 1000-unit minimum so it could not reach a layout the app had no answer for, which is a
    /// constraint the adaptive Shell removes the need for.
    /// </remarks>
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(ServiceHelper.GetRequiredService<AppShell>()) { Title = "BlogIt Admin" };
}
