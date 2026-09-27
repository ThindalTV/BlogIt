namespace BlogIt.MauiAdmin.Core.Navigation;

/// <summary>How much horizontal room the window currently has, in device-independent units.</summary>
public enum LayoutMode
{
    /// <summary>Phone-sized. The navigation menu stays collapsed behind its button.</summary>
    Narrow,

    /// <summary>Small tablet, a folded-open phone, or a part-screen desktop window.</summary>
    Medium,

    /// <summary>Desktop, or a tablet in landscape: room for a permanent navigation rail.</summary>
    Wide,
}

/// <summary>
/// Turns the window's current width into a <see cref="LayoutMode"/>.
/// </summary>
/// <remarks>
/// <para>
/// Width, deliberately — not <c>DeviceInfo.Idiom</c> and not the platform. A foldable is the case
/// that makes the difference concrete: a Galaxy Z Fold reports the phone idiom whether it is shut
/// (~340 units across) or open (~690), so an idiom-based layout gives an unfolded device the
/// layout of a shut one. Width also means a desktop window dragged narrow reflows like a tablet
/// instead of staying wide and cramped, which the previous layout explicitly refused to do.
/// </para>
/// <para>
/// The two thresholds are the standard Fluent/WinUI breakpoints. They land the Fold's two states
/// either side of <see cref="MediumMinimumWidth"/>, which is the split this app most needs to get
/// right, and they put a phone in <see cref="LayoutMode.Narrow"/> and a landscape tablet or any
/// ordinary desktop window in <see cref="LayoutMode.Wide"/>.
/// </para>
/// </remarks>
public static class LayoutBreakpoints
{
    /// <summary>At or above this width the navigation menu is worth opening; below it the window
    /// is phone-shaped.</summary>
    public const double MediumMinimumWidth = 640;

    /// <summary>At or above this width there is room to keep the navigation rail open permanently
    /// without squeezing the content beside it.</summary>
    public const double WideMinimumWidth = 1008;

    /// <summary>
    /// The layout for a window of <paramref name="width"/> device-independent units.
    /// </summary>
    /// <remarks>
    /// A non-positive or non-finite width means the window has not been measured yet — every
    /// platform reports one at least once before first layout. That answers
    /// <see cref="LayoutMode.Narrow"/> rather than throwing, because starting collapsed and
    /// expanding on the first real measurement is invisible, whereas starting with a locked rail
    /// and tearing it away is not.
    /// </remarks>
    public static LayoutMode ModeFor(double width)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0)
            return LayoutMode.Narrow;

        if (width >= WideMinimumWidth) return LayoutMode.Wide;
        if (width >= MediumMinimumWidth) return LayoutMode.Medium;
        return LayoutMode.Narrow;
    }
}
