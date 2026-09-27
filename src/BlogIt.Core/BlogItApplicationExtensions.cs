using BlogIt.Services;
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

        // Recorded before the contributors run, so startup validation can report on what the host
        // did rather than only on what BlogIt added. The antiforgery mark is ASP.NET's own, read the
        // same way BlogItPipelineOptions already reads the authentication and authorization ones.
        var state = application.ApplicationServices.GetService<BlogItRegistrationState>();
        if (state is not null)
        {
            state.UseCalled = true;
            state.AntiforgeryBeforeUseBlogIt =
                application.Properties.ContainsKey("__AntiforgeryMiddlewareSet");
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

        if (endpoints.ServiceProvider.GetService<BlogItRegistrationState>() is { } state)
        {
            state.MapCalled = true;
            // The live collection, not a copy: endpoints the host maps after this call still need to
            // be visible when the conflict check runs.
            state.DataSources = endpoints.DataSources;
        }

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

    /// <summary>
    /// Claims an unclaimed BlogIt site: creates the first administrator account and writes the
    /// initial settings, exactly as completing the setup wizard would.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if this call claimed the site; <see langword="false"/> if it had
    /// already been claimed and nothing was changed.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The request is invalid — an unusable site URL, a password that fails
    /// <see cref="Shared.Helpers.PasswordPolicy"/>, analytics without a tag container, and so on.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Call after <see cref="MigrateBlogItAsync"/> and before <c>Run()</c>. Until this (or the
    /// wizard) has run there is no user, and no user means no way to authenticate — which is why
    /// nothing could previously be provisioned without a human at a browser, blocking CI, end-to-end
    /// tests and container startup alike.
    /// </para>
    /// <para>
    /// Returning <see langword="false"/> rather than throwing on an already-claimed site is
    /// deliberate: the natural caller is an entry point that runs on every start, so being called
    /// again is the expected case and not an error. An invalid request <em>is</em> an error — it
    /// means the calling code is wrong, not that the site is in a particular state — so that throws.
    /// </para>
    /// <para>
    /// Safe to run concurrently. Two replicas racing to claim the same database resolve through the
    /// same setup lock the HTTP route uses; the loser simply gets <see langword="false"/>.
    /// </para>
    /// </remarks>
    public static async Task<bool> InitializeBlogItAsync(
        this IHost host,
        BlogItSetupRequest setup,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(setup);
        EnsureRegistered(host.Services);

        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ISetupService>();

        var result = await service
            .InitializeAsync(setup.ToWireRequest(), cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            SetupOutcome.Completed => true,
            SetupOutcome.AlreadyComplete => false,
            _ => throw new ArgumentException(
                "BlogIt setup was rejected: " + Describe(result.Errors), nameof(setup))
        };

        static string Describe(IReadOnlyDictionary<string, string[]>? errors) =>
            errors is null or { Count: 0 }
                ? "no details were reported."
                : string.Join(
                    " ",
                    errors.Select(error => $"{error.Key}: {string.Join(" ", error.Value)}"));
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
