namespace BlogIt;

/// <summary>
/// Adds the Google Analytics reporting provider to a BlogIt configuration.
/// </summary>
public static class GoogleAnalyticsExtensions
{
    /// <summary>
    /// Registers the Google Analytics Data API-backed <see cref="Services.IAnalyticsService"/>,
    /// enabling the admin dashboard's analytics panel.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Takes no configuration, for the same reason as <c>UseOpenAi</c>: the GA4 property ID and the
    /// service-account JSON are stored per site and edited in the admin's Settings screen, so a
    /// startup callback would only create a second source of truth for values the saved settings
    /// would then override.
    /// </para>
    /// <para>
    /// This is reporting only, and separate from the client-side measurement snippet: the
    /// <c>GaScript</c> component in the core package emits a GA tag from the saved measurement ID
    /// and needs no provider and no SDK.
    /// </para>
    /// </remarks>
    /// <param name="options">The options instance being configured inside <c>AddBlogIt</c>.</param>
    /// <returns>The same options instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">An analytics provider is already configured.</exception>
    public static BlogItOptions UseGoogleAnalytics(this BlogItOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseAnalyticsProvider(new GoogleAnalyticsProviderRegistration());
    }
}
