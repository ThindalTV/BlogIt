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
    /// The absolute route for this destination, e.g. <c>//settings</c>.
    /// </summary>
    /// <remarks>
    /// There is exactly one of these because there is exactly one Shell, and every destination is
    /// a top-level item in it at every window size. That is a deliberate constraint rather than an
    /// incidental one: destinations used to carry a second, phone-only route, because the phone
    /// Shell's tab bar held five items and an absolute <c>//settings</c> could not resolve against
    /// a hierarchy that did not contain it. Every item on the phone's More menu threw at runtime,
    /// which took site management — and so multi-blog use — with it. Keeping one route per
    /// destination means that class of drift cannot come back.
    /// </remarks>
    public string ShellRoute => $"//{Key}";
}
