namespace BlogIt.MauiAdmin.Core.Navigation;

/// <summary>
/// One navigable area of the admin client, described independently of any Shell.
/// </summary>
/// <param name="Key">Stable identifier, also the Shell route segment.</param>
/// <param name="Label">User-facing menu text.</param>
/// <param name="PageTypeName">
/// The <c>ContentPage</c> type that renders this destination. Held as a name rather than a
/// <see cref="Type"/> because the page types live in the MAUI assembly, which this library
/// deliberately does not reference.
/// </param>
public sealed record AppDestination(string Key, string Label, string PageTypeName)
{
    /// <summary>
    /// The absolute route used by shells that expose this destination as a top-level item
    /// (the desktop nav rail and the compact flyout), e.g. <c>//settings</c>.
    /// </summary>
    public string ShellRoute => $"//{Key}";

    /// <summary>
    /// The route used on phones, where this destination is not part of the Shell hierarchy and
    /// is reached from the More tab instead.
    /// </summary>
    /// <remarks>
    /// Prefixed rather than reusing <see cref="Key"/> so that registering it globally cannot
    /// collide with the identically-named <c>FlyoutItem</c> routes the other two shells declare
    /// for the same destination.
    /// <para>
    /// This exists because absolute <c>//route</c> navigation only resolves against the current
    /// Shell's own hierarchy. The phone Shell's TabBar holds five items and none of the
    /// secondary destinations, so <c>//settings</c> and friends threw at runtime there — the
    /// entire More menu was dead, taking multi-site management with it.
    /// </para>
    /// </remarks>
    public string PhoneRoute => $"more/{Key}";
}
