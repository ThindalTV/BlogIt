using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BlogIt;

public static class BlogItApplicationExtensions
{
    private const string MiddlewareConfiguredKey = "__BlogIt_MiddlewareConfigured";

    /// <summary>
    /// Adds BlogIt's redirect middleware, plus rate limiting, authentication and authorization in
    /// that order. Call this after host-level forwarding, error handling, HTTPS, and static-file
    /// middleware, and before antiforgery or endpoint execution.
    /// </summary>
    /// <remarks>
    /// A host that already calls <c>UseAuthentication</c>/<c>UseAuthorization</c> before this may
    /// keep doing so — BlogIt detects those and adds nothing on top. A host that adds them
    /// <em>after</em> <c>UseBlogIt</c>, or that calls <c>UseRateLimiter</c> itself, cannot be
    /// detected; use the <see cref="UseBlogIt(IApplicationBuilder, Action{BlogItPipelineOptions})"/>
    /// overload to opt out of the matching middleware.
    /// </remarks>
    public static IApplicationBuilder UseBlogIt(this IApplicationBuilder application) =>
        Use(application, configure: null);

    /// <summary>
    /// Adds BlogIt's middleware, choosing which of the pipeline-wide middleware BlogIt contributes
    /// and which the host owns.
    /// </summary>
    /// <param name="application">The host's pipeline.</param>
    /// <param name="configure">Configures <see cref="BlogItPipelineOptions"/>.</param>
    public static IApplicationBuilder UseBlogIt(
        this IApplicationBuilder application,
        Action<BlogItPipelineOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return Use(application, configure);
    }

    private static IApplicationBuilder Use(
        IApplicationBuilder application,
        Action<BlogItPipelineOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(application);
        EnsureRegistered(application.ApplicationServices);

        var pipelineOptions = new BlogItPipelineOptions();
        configure?.Invoke(pipelineOptions);
        // Carried on the pipeline rather than in DI: these are per-UseBlogIt-call decisions about one
        // IApplicationBuilder, while the middleware contributors are container singletons that could
        // be invoked for more than one pipeline.
        application.Properties[BlogItPipelineOptions.PropertyKey] = pipelineOptions;
        if (!application.Properties.TryAdd(MiddlewareConfiguredKey, true))
        {
            throw new InvalidOperationException(
                "UseBlogIt has already been called for this application pipeline.");
        }

        var contributors = application.ApplicationServices
            .GetServices<IBlogItMiddlewareContributor>()
            .ToArray();

        if (contributors.Length == 0)
        {
            throw MissingContributor("middleware", nameof(IBlogItMiddlewareContributor));
        }

        foreach (var contributor in contributors)
        {
            contributor.Configure(application);
        }

        return application;
    }

    public static IEndpointRouteBuilder MapBlogIt(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        EnsureRegistered(endpoints.ServiceProvider);

        var contributors = endpoints.ServiceProvider
            .GetServices<IBlogItEndpointContributor>()
            .ToArray();

        if (contributors.Length == 0)
        {
            throw MissingContributor("endpoint", nameof(IBlogItEndpointContributor));
        }

        foreach (var contributor in contributors)
        {
            contributor.MapEndpoints(endpoints);
        }

        return endpoints;
    }

    public static async Task MigrateBlogItAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        EnsureRegistered(host.Services);

        var migrators = host.Services.GetServices<IBlogItMigrator>().ToArray();
        if (migrators.Length == 0)
        {
            throw MissingContributor("migration", nameof(IBlogItMigrator));
        }

        foreach (var migrator in migrators)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void EnsureRegistered(IServiceProvider services)
    {
        if (services.GetService<BlogItRegistrationMarker>() is null)
        {
            throw new InvalidOperationException(
                "BlogIt is not registered. Call services.AddBlogIt(...) during application startup.");
        }
    }

    private static InvalidOperationException MissingContributor(string kind, string abstraction) =>
        new(
            $"No BlogIt {kind} contributor is registered. The BlogIt engine/provider implementation must register {abstraction} before this extension can be used.");
}
