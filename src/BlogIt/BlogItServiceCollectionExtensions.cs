using BlogIt.Services;
using BlogIt.Shared.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace BlogIt;

public static class BlogItServiceCollectionExtensions
{
    public static IServiceCollection AddBlogIt(
        this IServiceCollection services,
        Action<BlogItOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(BlogItRegistrationMarker)))
        {
            throw new InvalidOperationException(
                "AddBlogIt has already been called for this service collection. Configure all BlogIt options and providers in a single call.");
        }

        var configuredOptions = new BlogItOptions();
        configure(configuredOptions);
        configuredOptions.NormalizeValidateAndFreeze();

        services.AddSingleton<BlogItRegistrationMarker>();
        services.AddSingleton(configuredOptions);
        services.AddSingleton<IOptions<BlogItOptions>>(Options.Create(configuredOptions));
        services.AddSingleton(configuredOptions.DatabaseProvider);
        services.AddSingleton(configuredOptions.StorageProvider);

        RegisterProvider(
            services,
            "database",
            configuredOptions.DatabaseProvider.Name,
            configuredOptions.DatabaseProvider.RegisterServices);
        RegisterProvider(
            services,
            "storage",
            configuredOptions.StorageProvider.Name,
            configuredOptions.StorageProvider.RegisterServices);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISettingsService, SettingsService>();
        services.TryAddScoped<IAuthService, AuthService>();
        services.TryAddScoped<IAnalyticsService, AnalyticsService>();
        services.TryAddScoped<IAiService, AiService>();
        services.TryAddSingleton<IPreviewTokenService, PreviewTokenService>();
        services.TryAddSingleton<JwtSigningKeyCache>();
        services.TryAddSingleton<IUrlRedirectService, UrlRedirectService>();
        services.TryAddScoped<IPublicContentService, PublicContentService>();
        services.TryAddSingleton<BlogItAdminAssets>();
        services.AddHostedService<PublicationSchedulingService>();
        services.AddHttpContextAccessor();
        services.AddEndpointsApiExplorer();

        services.AddAuthentication()
            .AddJwtBearer(BlogItDefaults.AuthenticationScheme, _ => { });

        // Configured via the DI-aware overload (rather than inline in AddJwtBearer above) so
        // JwtSigningKeyCache — a singleton — can be captured once by reference and read from
        // IssuerSigningKeyResolver without touching HttpContext.RequestServices, which that
        // resolver delegate has no access to.
        services.AddOptions<JwtBearerOptions>(BlogItDefaults.AuthenticationScheme)
            .Configure<JwtSigningKeyCache>((options, keyCache) =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    // Resolves against JwtSigningKeyCache's own atomically-swapped snapshot
                    // instead of a field on this shared options instance, so validating a
                    // request never mutates state other concurrent requests are reading.
                    IssuerSigningKeyResolver = (_, _, _, _) => keyCache.ResolveKeys()
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = async context => await keyCache.RefreshAsync(),
                    OnTokenValidated = ValidateSecurityStampAsync
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(BlogItDefaults.AdminAuthorizationPolicy, policy =>
            {
                policy.AuthenticationSchemes.Add(BlogItDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });

        services.AddRateLimiter(options =>
        {
            options.OnRejected = (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return ValueTask.CompletedTask;
            };

            // Partitioned per client IP (falls back to a shared bucket when the IP is
            // unavailable) so one attacker guessing passwords can't lock out everyone else.
            options.AddPolicy(BlogItDefaults.LoginRateLimiterPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    }));
        });

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBlogItMiddlewareContributor, AdminAssetMiddlewareContributor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBlogItMiddlewareContributor, EngineMiddlewareContributor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBlogItEndpointContributor, EngineEndpointContributor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBlogItEndpointContributor, AdminAssetEndpointContributor>());

        return services;
    }

    /// <summary>
    /// Rejects a signed, unexpired token whose account has since been deleted, or whose sessions
    /// have been deliberately invalidated (see <c>AppUser.SecurityStamp</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A valid signature only proves the token was issued by this site at some point. Without
    /// this check nothing could end a session early: a changed password left every existing token
    /// working, and a deleted user kept full access until natural expiry — the default being 1440
    /// minutes, comfortably long enough to re-create the account through <c>POST /api/users</c>.
    /// </para>
    /// <para>
    /// The cost is one primary-key lookup per authenticated request, projected to the single
    /// stamp column. That is the deliberate trade for needing no denylist and no shared cache —
    /// which matters because the engine is single-instance today and a distributed store would be
    /// a new deployment requirement rather than a code change.
    /// </para>
    /// </remarks>
    private static async Task ValidateSecurityStampAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        // JwtSecurityTokenHandler maps `sub` onto NameIdentifier by default, so accept either.
        var subject = principal?.FindFirstValue("sub")
            ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var presentedStamp = principal?.FindFirstValue(BlogItClaimTypes.SecurityStamp);

        if (!Guid.TryParse(subject, out var userId) || string.IsNullOrEmpty(presentedStamp))
        {
            // Also covers tokens minted before the stamp existed: no claim, no access.
            context.Fail("Token is missing a usable subject or security stamp.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<BlogItDbContext>();
        var currentStamp = await db.Users
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        // Ordinal comparison, not fixed-time: the stamp is a change marker, never a credential.
        // Learning it buys an attacker nothing, since they would still have to sign a token.
        if (currentStamp is null || !string.Equals(currentStamp, presentedStamp, StringComparison.Ordinal))
            context.Fail("Token was issued for an account that no longer exists or has since been revoked.");
    }

    private static void RegisterProvider(
        IServiceCollection services,
        string kind,
        string name,
        Action<IServiceCollection> register)
    {
        try
        {
            register(services);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"The BlogIt {kind} provider '{name}' failed while registering its services.",
                exception);
        }
    }
}
