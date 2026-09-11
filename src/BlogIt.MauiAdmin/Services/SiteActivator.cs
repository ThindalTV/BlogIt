using BlogIt.MauiAdmin.Core.Navigation;
using BlogIt.MauiAdmin.Core.Sites;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// Switching to a blog: makes it the active site and navigates wherever it actually needs to go.
/// </summary>
/// <remarks>
/// <para>
/// One implementation because there are two entry points — the Sites list and the site switcher
/// pinned above the navigation menu — and they had drifted. The list re-probed and routed to login
/// or first-run setup as needed; the switcher just set the active site and went to the dashboard.
/// Picking an expired site from the switcher therefore landed on a dashboard that fired six
/// parallel requests, every one of them a 401, before the auth handler recovered the user to the
/// login screen they should have been sent to in the first place.
/// </para>
/// <para>
/// Whether setup is complete is never trusted from a cached flag: a site that was mid-setup when
/// it was added may well have been finished since, so a site without a usable token is re-probed
/// live every time.
/// </para>
/// </remarks>
public sealed class SiteActivator(
    SiteProfileService profileService,
    SiteProbeService probeService,
    IDialogService dialogService)
{
    public async Task ActivateAsync(SiteProfile site)
    {
        if (site.IsTokenValid)
        {
            await profileService.SetActiveAsync(site.Id);
            await Shell.Current.GoToAsync(AppNavigation.RouteTo("dashboard"));
            return;
        }

        var result = await probeService.ProbeAsync(site.BaseUri, site.ApiPath);
        switch (result.Status)
        {
            case SiteProbeStatus.ReachableSetupComplete:
                // Deliberately not activated yet — LoginViewModel does that once the credentials
                // are actually accepted, so a failed sign-in cannot leave the app pointed at a
                // site it has no session for.
                await Shell.Current.GoToAsync($"sites/login?id={site.Id}");
                break;

            case SiteProbeStatus.ReachableSetupIncomplete:
                await Shell.Current.GoToAsync($"sites/setup-required?id={site.Id}");
                break;

            default:
                await dialogService.AlertAsync(
                    "Can't reach this site",
                    "Check the domain, port, and your connection, then try again.");
                break;
        }
    }
}
