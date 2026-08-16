using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;

namespace BlogIt.Admin.Services;

/// <summary>
/// The admin's single point of token handling: attaches the stored bearer token on the way out and
/// reacts to a rejected token on the way back.
/// </summary>
/// <remarks>
/// <para>
/// Replaces a hand-called <c>PrepareAuthAsync</c> that sat at the top of ~28
/// <see cref="ApiClient"/> methods and set <c>HttpClient.DefaultRequestHeaders.Authorization</c>.
/// That was shared mutable state on a client several components call concurrently (the dashboard
/// fires three list requests through <c>Task.WhenAll</c>), and any method added later that forgot
/// the call silently sent an anonymous request. The header now belongs to the request message.
/// </para>
/// <para>
/// It also closes the gap that made an expired session look live: the expiry check in
/// <see cref="AuthStateProvider.GetAuthenticationStateAsync"/> runs once and its result is cached
/// until <c>NotifyAuthenticationStateChanged</c> fires, and nothing fired it except login and
/// logout — so the sidebar kept showing the username and <c>[Authorize]</c> pages kept rendering
/// while every request underneath failed. Token revocation (a password change, a user delete, a JWT
/// secret rotation) makes that a state the admin reaches immediately rather than only by waiting
/// out an expiry.
/// </para>
/// <para>
/// Ported from <c>BlogIt.MauiAdmin.Services.ActiveSiteHttpMessageHandler</c>, which does the same
/// job. It publishes a message for a top-level subscriber to act on because MAUI has one shell for
/// many sites; the admin is a single site, so the redirect happens here directly.
/// </para>
/// </remarks>
public sealed class AdminAuthMessageHandler(
    AuthStateProvider authStateProvider,
    NavigationManager navigation) : DelegatingHandler
{
    /// <summary>
    /// Endpoints that answer 401 as a normal outcome rather than as a rejected session:
    /// <c>auth/login</c> returns it for bad credentials. Treating those as an expired session would
    /// wipe storage and bounce the login page onto itself with <c>returnUrl=login</c>.
    /// </summary>
    private static readonly string[] AnonymousEndpoints = ["auth/login", "setup/"];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isAnonymousEndpoint = IsAnonymousEndpoint(request.RequestUri);

        // An explicit header on the request wins — nothing sets one today, but silently
        // overwriting a caller's credentials would be a nasty surprise if something ever does.
        if (!isAnonymousEndpoint && request.Headers.Authorization is null)
        {
            var token = await authStateProvider.GetTokenAsync();
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !isAnonymousEndpoint)
        {
            // 401 only. 403 means the token was accepted and the user simply is not permitted, so
            // signing them out there would lock an editor out of the whole admin over one screen.
            await SignOutRejectedSessionAsync();
        }

        // Returned unchanged: the caller still needs to see the failure, so a screen that was
        // mid-save shows its error rather than appearing to have succeeded.
        return response;
    }

    private async Task SignOutRejectedSessionAsync()
    {
        var loginUrl = AdminLoginRedirect.BuildLoginUrl(
            navigation.ToBaseRelativePath(navigation.Uri));

        // Order is deliberate. Clearing the token first means the login page's own
        // "already signed in?" check cannot bounce us back to the dashboard. Navigating before
        // publishing the anonymous state means the current [Authorize] page is already gone when
        // the state lands, so AuthorizeRouteView never renders NotAuthorized and RedirectToLogin
        // never races this navigation with a bare "login" that would drop the returnUrl.
        await authStateProvider.ClearStoredTokenAsync();
        navigation.NavigateTo(loginUrl, replace: true);
        authStateProvider.NotifySignedOut();
    }

    private static bool IsAnonymousEndpoint(Uri? requestUri)
    {
        if (requestUri is null)
            return false;

        var path = requestUri.IsAbsoluteUri ? requestUri.AbsolutePath : requestUri.OriginalString;
        return AnonymousEndpoints.Any(endpoint =>
            path.Contains(endpoint, StringComparison.OrdinalIgnoreCase));
    }
}
