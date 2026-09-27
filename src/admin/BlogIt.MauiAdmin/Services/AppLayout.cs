using BlogIt.MauiAdmin.Core.Navigation;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// The current navigation layout, kept up to date from the window's width.
/// </summary>
/// <remarks>
/// <para>
/// This was a static property with an initializer, read once per process from
/// <c>DeviceInfo.Platform</c> and <c>DeviceInfo.Idiom</c>. Two things were wrong with that. It
/// could not change, so unfolding a foldable did nothing at all — and it asked the wrong
/// question, because idiom cannot distinguish a shut Fold from an open one, and says nothing
/// about a desktop window the user has dragged narrow.
/// </para>
/// <para>
/// An instance with an event, rather than a static with one, so it can be injected into the Shell
/// and substituted in a test. The width-to-mode decision itself lives in
/// <see cref="LayoutBreakpoints"/> in the Core library, where it is covered without a device.
/// </para>
/// </remarks>
public sealed class AppLayout
{
    /// <summary>
    /// The layout for the width last reported. Starts at <see cref="LayoutMode.Narrow"/>, matching
    /// <see cref="LayoutBreakpoints.ModeFor"/> on an unmeasured window: expanding on the first
    /// real measurement is invisible, where collapsing a rail that was already drawn is not.
    /// </summary>
    public LayoutMode Mode { get; private set; } = LayoutMode.Narrow;

    /// <summary>Raised only when <see cref="Mode"/> actually changes, so a resize drag does not
    /// re-apply the same layout on every frame.</summary>
    public event Action<LayoutMode>? ModeChanged;

    /// <summary>Reports the window's current width. Safe to call as often as the platform fires.</summary>
    public void UpdateForWidth(double width)
    {
        var next = LayoutBreakpoints.ModeFor(width);
        if (next == Mode) return;

        Mode = next;
        ModeChanged?.Invoke(next);
    }
}
