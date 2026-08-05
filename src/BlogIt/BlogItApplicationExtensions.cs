using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BlogIt;

public static class BlogItApplicationExtensions
{
    private const string MiddlewareConfiguredKey = "__BlogIt_MiddlewareConfigured";

    /// <summary>
    /// Adds BlogIt's redirect, authentication, and authorization middleware in that order.
    /// Call this after host-level forwarding, error handling, HTTPS, and static-file middleware,
    /// and before antiforgery or endpoint execution. Do not separately add authentication or
    /// authorization middleware for BlogIt.
    /// </summary>
    public static IApplicationBuilder UseBlogIt(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        EnsureRegistered(application.ApplicationServices);
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
