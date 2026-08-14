namespace BlogIt.MauiAdmin.Messages;

/// <summary>Published by <see cref="Services.ActiveSiteHttpMessageHandler"/> when a
/// call against a site returns 401. A top-level subscriber reacts by navigating to
/// that site's login screen — kept decoupled from the handler, which has no
/// navigation concerns of its own.</summary>
public sealed class SiteAuthExpiredMessage(string siteId)
{
    public string SiteId { get; } = siteId;
}
