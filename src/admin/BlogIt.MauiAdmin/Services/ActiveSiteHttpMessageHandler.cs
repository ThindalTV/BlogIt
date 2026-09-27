using System.Net;
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
public class ActiveSiteHttpMessageHandler(SiteProfileService profileService) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var profile = await profileService.GetActiveProfileAsync();
            if (profile is not null)
            {
                await profileService.ClearTokenAsync(profile.Id);
                WeakReferenceMessenger.Default.Send(new SiteAuthExpiredMessage(profile.Id));
            }
        }

        return response;
    }
}
