using System.Net;
using BlogIt.MauiAdmin.Core.Sites;
using BlogIt.MauiAdmin.Messages;
using CommunityToolkit.Mvvm.Messaging;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// Reacts to a 401 from any site by clearing that site's stored token and
/// publishing <see cref="SiteAuthExpiredMessage"/>, so a top-level subscriber can
/// navigate to that site's login screen. BaseAddress and the bearer token are set
/// up earlier, in <see cref="MauiApiClient"/>'s CreateActiveSiteClientAsync — not
/// here — because HttpClient validates/combines a relative RequestUri against
/// BaseAddress inside HttpClient.SendAsync itself, before the request ever reaches
/// this handler's SendAsync override.
/// </summary>
/// <remarks>
/// Which site, and whether the 401 still means anything, are decided by
/// <see cref="SiteAuthExpiry"/> from the request itself — see its remarks for the two races that
/// avoids.
/// </remarks>
public class ActiveSiteHttpMessageHandler(SiteProfileService profileService) : DelegatingHandler
{
    // One gate for every site: the check-then-clear below must not interleave with a concurrent
    // 401, or two requests can both see the token still stored and both publish the message. A 401
    // is rare enough that serialising them across sites costs nothing.
    private static readonly SemaphoreSlim ExpiryGate = new(1, 1);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            await ExpireSessionAsync(request);

        return response;
    }

    private async Task ExpireSessionAsync(HttpRequestMessage request)
    {
        var profile = SiteAuthExpiry.ProfileForRequest(
            request.RequestUri, await profileService.GetProfilesAsync());
        if (profile is null)
            return;

        var sentToken = request.Headers.Authorization is { Scheme: "Bearer" } authorization
            ? authorization.Parameter
            : null;

        await ExpiryGate.WaitAsync();
        try
        {
            if (!SiteAuthExpiry.ShouldExpire(sentToken, await profileService.GetTokenAsync(profile.Id)))
                return;

            await profileService.ClearTokenAsync(profile.Id);
        }
        finally
        {
            ExpiryGate.Release();
        }

        WeakReferenceMessenger.Default.Send(new SiteAuthExpiredMessage(profile.Id));
    }
}
