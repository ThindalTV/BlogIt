namespace BlogIt.MauiAdmin.Core.Navigation;

/// <summary>Which family of Shell is currently hosting the app.</summary>
public enum ShellLayout
{
    /// <summary>Tab bar of primary destinations; everything else is reached from the More tab.</summary>
    Phone,

    /// <summary>Flyout or locked nav rail listing every destination as a top-level item.</summary>
    Flyout,
}

/// <summary>
/// Turns a destination key into the route that actually resolves in the current Shell.
/// </summary>
/// <remarks>
/// Exists because the correct route for a destination is not a property of the destination —
/// it depends on which Shell is loaded. <c>//sites</c> is right under a flyout and throws on a
/// phone, where the same screen has to be pushed as a registered route. Every navigation to a
/// destination should go through here rather than hard-coding a literal, which is the mistake
/// that left the whole phone More menu dead.
/// </remarks>
public sealed class DestinationRouter(ShellLayout layout)
{
    public ShellLayout Layout { get; } = layout;

    /// <summary>The route to navigate to reach <paramref name="key"/> in this layout.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The key is not a known destination.</exception>
    public string RouteTo(string key)
    {
        var destination = AppNavigation.Find(key);

        // Primary destinations are tabs on the phone, so they keep the absolute route there;
        // only the secondary ones live outside the phone Shell's hierarchy.
        var isSecondary = AppNavigation.Secondary.Any(d => d.Key == destination.Key);

        return Layout == ShellLayout.Phone && isSecondary
            ? destination.PhoneRoute
            : destination.ShellRoute;
    }
}
