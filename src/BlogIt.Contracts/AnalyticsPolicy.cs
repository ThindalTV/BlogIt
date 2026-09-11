namespace BlogIt.Shared.Helpers;

/// <summary>
/// The single definition of what BlogIt accepts as analytics configuration: what a Google Tag
/// Manager container ID has to look like, and the rule that GA4 reporting may only be configured
/// alongside one. Applied by the setup wizard, the settings screen, and — as the authority — by
/// <c>SetupApi</c> and <c>SettingsApi</c> on the way in.
/// </summary>
/// <remarks>
/// <para>
/// BlogIt has two analytics halves. The container ID drives the Google Tag Manager snippet that
/// <c>GaScript</c> writes into the page; the property ID and service-account JSON drive the GA4
/// Data API reporting the admin dashboard reads back. Nothing used to connect them, so a site could
/// be configured to report on traffic it had no tag to collect — and, more to the point, a site
/// could collect traffic through a route that never passed the container.
/// </para>
/// <para>
/// The container is where cookie consent is configured, which is what makes the ordering a rule
/// rather than a nicety: tracking that does not go through the container has not been consented to.
/// Making reporting depend on a container ID collapses the two entry points into one — configure
/// the tag, then configure what reads it back — so there is no way to end up with analytics whose
/// collection side was never set up.
/// </para>
/// <para>
/// It lives in the contracts assembly for the same reason as <see cref="PasswordPolicy"/>: the
/// admin client and the server run <em>the same code</em> rather than two copies of the same rule.
/// The client checks so the wizard can grey out a step the user cannot use yet; the server checks
/// again on arrival and remains the authority.
/// </para>
/// </remarks>
public static class AnalyticsPolicy
{
    /// <summary>
    /// The prefix every Google Tag Manager container ID carries. Worth checking, because the
    /// mistake it catches is silent: a GA4 measurement ID (<c>G-…</c>) looks close enough to belong
    /// here, but the container loader this ID is interpolated into — <c>gtm.js?id=…</c> — is not
    /// the endpoint that serves one, so the page would load a tag that never fires.
    /// </summary>
    public const string ContainerIdPrefix = "GTM-";

    /// <summary>What a caller is told when the container ID is not shaped like one.</summary>
    public const string MalformedContainerIdMessage =
        "Container ID must be a Google Tag Manager container ID — 'GTM-' followed by letters and "
        + "digits, for example GTM-WQCQZBKK. A GA4 measurement ID (G-…) is a different thing and "
        + "will not load a container.";

    /// <summary>
    /// What a caller is told when reporting is configured without a container ID. Phrased for a
    /// person filling in a settings form, since that is where it surfaces.
    /// </summary>
    public const string ReportingRequiresTagMessage =
        "Analytics reporting requires a Container ID. Enter the Google Tag Manager container ID "
        + "first: the container is where visitor consent is collected, so reporting configured "
        + "without one would read traffic that was never consented to.";

    /// <summary>Whether a container is configured — the precondition for everything else here.</summary>
    public static bool HasTag(string? containerId) =>
        !string.IsNullOrWhiteSpace(containerId);

    /// <summary>
    /// Whether the caller is asking for Data API reporting at all. Either field on its own counts:
    /// half-configured reporting is still reporting the admin will try to use.
    /// </summary>
    public static bool IsReportingConfigured(string? propertyId, string? credentialsJson) =>
        !string.IsNullOrWhiteSpace(propertyId) || !string.IsNullOrWhiteSpace(credentialsJson);

    /// <summary>
    /// Whether <paramref name="containerId"/> is shaped like a container ID. A blank value is
    /// <see langword="true"/> here — "not configured" is not "malformed", and
    /// <see cref="HasTag"/> is the question that separates them.
    /// </summary>
    public static bool IsWellFormedContainerId(string? containerId)
    {
        if (!HasTag(containerId))
            return true;

        var trimmed = containerId!.Trim();
        return trimmed.StartsWith(ContainerIdPrefix, StringComparison.OrdinalIgnoreCase)
            && trimmed.Length > ContainerIdPrefix.Length
            && trimmed[ContainerIdPrefix.Length..].All(char.IsAsciiLetterOrDigit);
    }

    /// <summary>
    /// The error for this combination of analytics settings, or <see langword="null"/> when it is
    /// allowed. The three arguments must be the <em>effective</em> values — what will be stored once
    /// the write lands — not just the fields a partial update happened to carry.
    /// </summary>
    public static string? Validate(string? containerId, string? propertyId, string? credentialsJson)
    {
        // Shape first: told that the ID is malformed, a caller who meant to configure reporting can
        // fix the one field and get both. Told only that reporting needs a container ID, they would
        // look at a container ID that is already filled in.
        if (!IsWellFormedContainerId(containerId))
            return MalformedContainerIdMessage;

        return !HasTag(containerId) && IsReportingConfigured(propertyId, credentialsJson)
            ? ReportingRequiresTagMessage
            : null;
    }
}
