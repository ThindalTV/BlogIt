using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlogIt;

/// <summary>
/// Checks, once the pipeline is built, that the host wired BlogIt up in a way that can actually
/// work — and says what to change when it did not.
/// </summary>
/// <remarks>
/// <para>
/// BlogIt's integration rules used to live only in documentation: call <c>UseBlogIt</c> after static
/// files, <c>MapBlogIt</c> after that, and do not map a route BlogIt already claims. Every one of
/// those failures is silent. Forgetting <c>UseBlogIt</c> leaves the application booting normally
/// with redirects that never fire; mapping your own <c>/sitemap.xml</c> throws
/// <c>AmbiguousMatchException</c> on the first request rather than at startup; a hand-written
/// <c>wwwroot/sitemap.xml</c> is simply never served, with nothing anywhere explaining why.
/// </para>
/// <para>
/// Implemented as an <see cref="IStartupFilter"/> rather than a hosted service for two reasons: it
/// runs while the pipeline is being built, so throwing aborts startup before the server binds a port
/// and no request is ever served against a broken configuration; and it is never invoked outside a
/// web host, so a console application that only calls <c>AddBlogIt</c> and <c>MigrateBlogItAsync</c>
/// is unaffected without having to sniff for a server.
/// </para>
/// </remarks>
internal sealed class BlogItStartupValidator(
    BlogItRegistrationState state,
    BlogItOptions options,
    IWebHostEnvironment environment,
    ILogger<BlogItStartupValidator> logger) : IStartupFilter
{
    /// <summary>The root documents BlogIt can be told not to serve, with the option that does it.</summary>
    private static readonly (string Path, string Option)[] RootDocuments =
    [
        ("/rss.xml", nameof(BlogItOptions.ServeRssFeed)),
        ("/atom.xml", nameof(BlogItOptions.ServeAtomFeed)),
        ("/sitemap.xml", nameof(BlogItOptions.ServeSitemap)),
        ("/robots.txt", nameof(BlogItOptions.ServeRobotsTxt))
    ];

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        builder =>
        {
            next(builder);
            Validate();
        };

    private void Validate()
    {
        EnsureWiredUp();
        LogRedirectSourcePolicy();
        WarnAboutAntiforgeryOrder();
        WarnAboutShadowedStaticFiles();
        EnsureNoRouteConflicts();
    }

    /// <summary>
    /// Fails when <c>AddBlogIt</c> ran but the pipeline calls were never made.
    /// </summary>
    /// <remarks>
    /// The <c>UseBlogIt</c> case is the valuable one. Without it the redirect middleware is absent,
    /// but <c>WebApplication</c> inserts authentication and authorization by itself when those
    /// services are registered — so the site starts, the admin portal works, and the only symptom is
    /// that no redirect ever fires. That is indistinguishable from an empty redirect table.
    /// </remarks>
    private void EnsureWiredUp()
    {
        if (!state.UseCalled)
        {
            throw new InvalidOperationException(
                "BlogIt is registered but UseBlogIt was never called, so its middleware is not in "
                + "the pipeline and URL redirects will never run. Call app.UseBlogIt() after "
                + "static-file middleware and before app.MapBlogIt().");
        }

        if (!state.MapCalled)
        {
            throw new InvalidOperationException(
                "BlogIt is registered but MapBlogIt was never called, so no BlogIt endpoint is "
                + "reachable — the admin portal, the API and the feeds are all unmapped. Call "
                + "app.MapBlogIt() after app.UseBlogIt().");
        }
    }

    /// <summary>
    /// States the effective redirect-source policy in the boot log.
    /// </summary>
    /// <remarks>
    /// Informational and unconditional, deliberately. The unrestricted default is a considered
    /// choice — the feature exists largely to honour inbound links to URLs the blog never owned — so
    /// warning about it would be warning about intended behaviour, and a warning an operator is
    /// supposed to ignore teaches them to ignore warnings. Conditioning it on something like "more
    /// than one user exists" would be worse still: it would make a configuration message depend on
    /// content-database state, read at the one moment that state is least likely to be final.
    /// </remarks>
    private void LogRedirectSourcePolicy()
    {
        if (options.RedirectSourcePrefixes.Count == 0)
        {
            logger.LogInformation(
                "BlogIt redirect sources are unrestricted: any path BlogIt has not reserved may be "
                + "claimed as a redirect source. Set BlogItOptions.RedirectSourcePrefixes to "
                + "confine them to particular path prefixes.");
        }
        else
        {
            logger.LogInformation(
                "BlogIt redirect sources are restricted to: {Prefixes}.",
                string.Join(", ", options.RedirectSourcePrefixes));
        }
    }

    /// <remarks>
    /// A warning rather than an error: BlogIt's own endpoints carry no antiforgery metadata and the
    /// media upload route opts out explicitly, so early placement is a deviation from the documented
    /// order rather than a proven break — and a host with its own Razor Pages forms may genuinely
    /// need it earlier. Throwing here would break working applications to enforce a convention.
    /// </remarks>
    private void WarnAboutAntiforgeryOrder()
    {
        if (state.AntiforgeryBeforeUseBlogIt)
        {
            logger.LogWarning(
                "UseAntiforgery was called before UseBlogIt. BlogIt expects antiforgery after it. "
                + "Nothing in BlogIt requires antiforgery today, so this is unlikely to break "
                + "anything, but the documented order is UseStaticFiles, UseBlogIt, UseAntiforgery, "
                + "MapBlogIt.");
        }
    }

    /// <summary>
    /// Warns when a file in the web root can never be served because BlogIt claims the same path.
    /// </summary>
    /// <remarks>
    /// Counter-intuitive enough to be worth saying out loud: the host calls <c>UseStaticFiles</c>
    /// before <c>UseBlogIt</c>, so the file "should" win — but routing is inserted at the top of the
    /// pipeline and the static-file middleware stands aside once an endpoint has matched, so BlogIt's
    /// endpoint takes the request and the file is dead. Only the default web root is visible here; a
    /// second <c>UseStaticFiles</c> with its own file provider cannot be inspected.
    /// </remarks>
    private void WarnAboutShadowedStaticFiles()
    {
        var fileProvider = environment.WebRootFileProvider;

        foreach (var (path, option) in RootDocuments)
        {
            if (!IsServed(path))
                continue;

            if (fileProvider.GetFileInfo(path.TrimStart('/')).Exists)
            {
                logger.LogWarning(
                    "wwwroot{Path} will never be served: BlogIt maps {Path} and endpoint routing "
                    + "runs before static files. Set BlogItOptions.{Option} = false to serve your "
                    + "own file instead.",
                    path,
                    path,
                    option);
            }
        }
    }

    /// <summary>
    /// Fails when the host maps a route BlogIt has already mapped.
    /// </summary>
    /// <remarks>
    /// Without this the duplicate is only discovered when someone requests the path and routing
    /// raises <c>AmbiguousMatchException</c> — a 500 on a URL that worked in development, with an
    /// exception that names neither BlogIt nor the option that resolves it.
    /// </remarks>
    private void EnsureNoRouteConflicts()
    {
        if (state.DataSources is null)
            return;

        List<RouteEndpoint> endpoints;
        try
        {
            endpoints = state.DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .ToList();
        }
        catch (Exception ex)
        {
            // Building endpoints early is legal — routing does it moments later — but it is the one
            // check here that depends on how the host assembled its data sources. If it cannot be
            // done, say so rather than failing startup over a diagnostic.
            logger.LogWarning(
                ex,
                "BlogIt could not inspect the application's endpoints, so it did not check for "
                + "routes mapped by both BlogIt and this application. A duplicate route will "
                + "surface as an AmbiguousMatchException on the first request to it.");
            return;
        }

        foreach (var group in endpoints.GroupBy(
                     endpoint => endpoint.RoutePattern.RawText ?? string.Empty,
                     StringComparer.OrdinalIgnoreCase))
        {
            var blogIt = group.FirstOrDefault(
                endpoint => endpoint.Metadata.GetMetadata<BlogItEndpointMetadata>() is not null);
            if (blogIt is null)
                continue;

            var host = group.FirstOrDefault(
                endpoint => endpoint.Metadata.GetMetadata<BlogItEndpointMetadata>() is null
                            && MethodsOverlap(blogIt, endpoint));
            if (host is null)
                continue;

            var hint = blogIt.Metadata.GetMetadata<BlogItEndpointMetadata>()!.DisableHint;
            throw new InvalidOperationException(
                $"Both BlogIt and this application map '{group.Key}'. Routing cannot choose between "
                + "them and would throw AmbiguousMatchException on the first request. "
                + (hint is null
                    ? "Remove the application's mapping, or move BlogIt's routes by changing "
                      + "BlogItOptions.ApiPath, AdminPath or MediaPath."
                    : $"Set BlogItOptions.{hint} = false to leave the route to this application, "
                      + "or remove the application's mapping."));
        }
    }

    /// <summary>
    /// Whether two endpoints on the same pattern could both match one request method.
    /// </summary>
    /// <remarks>
    /// An endpoint with no method metadata answers every method, so it overlaps with anything.
    /// </remarks>
    private static bool MethodsOverlap(Endpoint first, Endpoint second)
    {
        var firstMethods = first.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
        var secondMethods = second.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;

        if (firstMethods is null or { Count: 0 } || secondMethods is null or { Count: 0 })
            return true;

        return firstMethods.Intersect(secondMethods, StringComparer.OrdinalIgnoreCase).Any();
    }

    private bool IsServed(string path) => path switch
    {
        "/rss.xml" => options.ServeRssFeed,
        "/atom.xml" => options.ServeAtomFeed,
        "/sitemap.xml" => options.ServeSitemap,
        "/robots.txt" => options.ServeRobotsTxt,
        _ => false
    };
}
