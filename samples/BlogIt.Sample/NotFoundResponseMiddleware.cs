using BlogIt.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace BlogIt.Sample;

/// <summary>
/// Renders a real HTTP 404 with real, site-styled HTML for public content pages
/// (<c>BlogPostPage</c>/<c>CustomPage</c>) that don't resolve to published content.
/// </summary>
/// <remarks>
/// Two more "obvious" approaches were tried first and both hit hard, confirmed issues in this
/// .NET version's Blazor static SSR pipeline:
/// <list type="bullet">
/// <item>Setting <c>HttpContext.Response.StatusCode = 404</c> directly from inside a page
/// component's <c>OnInitializedAsync</c>, then letting it render normally: the status applies,
/// but <c>RazorComponentEndpointInvoker</c> discards the component's rendered body whenever the
/// final status is exactly 404 (verified empirically — the identical code path with a different
/// status, e.g. 410, renders its body fine; only 404 is special-cased, treating it as "render
/// the framework's own NotFound content instead," the same handling as an unmatched route).
/// Checking the response afterward in wrapping middleware doesn't help either: by the time
/// <c>next()</c> returns, <c>HasStarted</c> is already true for any Razor Components response —
/// success or not — so nothing downstream can revise it.</item>
/// <item><c>NavigationManager.NotFound()</c> + <c>UseStatusCodePagesWithReExecute</c> (the
/// pairing Microsoft's docs describe for this scenario): status applies, but the re-executed
/// request throws <c>InvalidOperationException: 'HttpNavigationManager' already initialized</c>
/// — re-execution reuses the original request's DI scope, and NavigationManager (scoped,
/// initialize-once) was already initialized while rendering the original page.</item>
/// </list>
/// Buffering <c>Response.Body</c> into a <see cref="MemoryStream"/> for the duration of
/// <paramref name="next"/> sidesteps both: the real response never actually starts (nothing
/// touches the live transport until this middleware explicitly writes to it afterward), so the
/// status code can still be changed once rendering has finished, and — for the not-found case —
/// the buffered (empty) output is discarded entirely in favor of
/// <see cref="Components.NotFoundDocument"/> rendered fresh via <see cref="HtmlRenderer"/> with
/// its own DI scope, which never touches the original request's NavigationManager.
/// </remarks>
public sealed class NotFoundResponseMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
{
    public const string ItemKey = "BlogIt.ContentNotFound";

    /// <summary>
    /// Whether this request's response has to be buffered so its status and body can still be
    /// rewritten after rendering.
    /// </summary>
    /// <remarks>
    /// Only a rendered Razor component page can raise <see cref="ItemKey"/>, so only a request routed
    /// to one needs buffering. Everything else — <c>MediaProxyApi</c>'s streaming download, the
    /// admin API, static files, unmatched paths — is passed through untouched.
    /// <para>
    /// This scoping is not an optimisation. Buffering ran in front of the entire pipeline, which read
    /// every media download in full into an unbounded <see cref="MemoryStream"/> before a single byte
    /// reached the client: the response size became the memory cost per concurrent request, and the
    /// incremental delivery a video needs (this sample ships an .mp4 precisely to demonstrate it) was
    /// gone even though <c>MediaProxyApi</c> enables range processing.
    /// </para>
    /// <para>
    /// Decided on the matched endpoint rather than the request path because the content routes are
    /// slug-driven and cannot be pattern-matched from here, and rather than on the Accept header
    /// because a browser navigating straight to a media URL sends <c>text/html</c> too. Reading the
    /// endpoint requires routing to have run first, which is why <c>Program.cs</c> calls
    /// <c>UseRouting()</c> explicitly before registering this middleware.
    /// </para>
    /// </remarks>
    public static bool ShouldBufferResponse(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is not null;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldBufferResponse(context))
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        if (!context.Items.TryGetValue(ItemKey, out var flag) || flag is not true)
        {
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody);
            return;
        }

        var title = System.Text.RegularExpressions.Regex.IsMatch(context.Request.Path.Value ?? "", @"^/\d{4}/")
            ? "Post not found"
            : "Page not found";

        using var scope = context.RequestServices.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        var siteName = await settings.GetAsync(BlogIt.Shared.SettingKeys.SiteName) ?? "BlogIt";

        using var htmlRenderer = new HtmlRenderer(scope.ServiceProvider, loggerFactory);
        var html = await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(Components.NotFoundDocument.Title)] = title,
                [nameof(Components.NotFoundDocument.SiteName)] = siteName,
                [nameof(Components.NotFoundDocument.Year)] = DateTime.UtcNow.Year,
            });
            var output = await htmlRenderer.RenderComponentAsync<Components.NotFoundDocument>(parameters);
            return output.ToHtmlString();
        });

        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = null;
        await originalBody.WriteAsync(System.Text.Encoding.UTF8.GetBytes(html));
    }
}
