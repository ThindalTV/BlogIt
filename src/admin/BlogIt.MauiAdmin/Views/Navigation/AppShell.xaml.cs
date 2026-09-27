using BlogIt.MauiAdmin.Core.Navigation;
using BlogIt.MauiAdmin.Services;

namespace BlogIt.MauiAdmin.Views.Navigation;

/// <summary>
/// The app's only Shell. Re-applies its navigation layout whenever the window changes width,
/// without rebuilding anything.
/// </summary>
/// <remarks>
/// Setting <see cref="Microsoft.Maui.Controls.Shell.FlyoutBehavior"/> is the whole mechanism. It
/// is a bindable property on the live Shell, so changing it re-renders the navigation chrome and
/// leaves the page tree — and therefore the navigation stack, every ViewModel, and any half-typed
/// post — completely untouched. That is what lets a Fold be opened mid-sentence.
/// </remarks>
public partial class AppShell : Microsoft.Maui.Controls.Shell
{
    private readonly AppLayout _layout;

    public AppShell(AppLayout layout)
    {
        InitializeComponent();

        _layout = layout;
        _layout.ModeChanged += ApplyLayout;

        // The Shell fills the window, so its own laid-out width is the authoritative number and
        // needs no Window plumbing to reach. This fires on a desktop resize drag, on rotation, and
        // on a fold — the Android activity survives the last of those because MainActivity
        // declares ScreenSize and ScreenLayout among its ConfigurationChanges.
        SizeChanged += (_, _) => _layout.UpdateForWidth(Width);

        ApplyLayout(_layout.Mode);
    }

    private void ApplyLayout(LayoutMode mode) =>
        FlyoutBehavior = mode switch
        {
            // Wide enough that a permanent rail costs the content nothing.
            LayoutMode.Wide => FlyoutBehavior.Locked,

            // Anything narrower keeps the menu behind its button. Note this is the same behaviour
            // for Narrow and Medium today: the difference between them is reserved for the
            // content-side layout work, and is kept as a distinct mode so that work does not have
            // to reintroduce a breakpoint of its own.
            _ => FlyoutBehavior.Flyout,
        };

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Detaching from the window means this Shell is going away; without this the layout
        // service, which is a singleton, would hold it alive through the event.
        if (Handler is null)
            _layout.ModeChanged -= ApplyLayout;
    }
}
