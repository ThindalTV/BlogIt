using System.Text;
using BlogIt.Services;
using BlogIt.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

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
        services.TryAddSingleton<IUrlRedirectService, UrlRedirectService>();
        services.TryAddScoped<IPublicContentService, PublicContentService>();
        services.TryAddSingleton<BlogItAdminAssets>();
        services.AddHostedService<PublicationSchedulingService>();
        services.AddHttpContextAccessor();
        services.AddEndpointsApiExplorer();

        services.AddAuthentication()
            .AddJwtBearer(BlogItDefaults.AuthenticationScheme, options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = async context =>
                    {
                        var settings = context.HttpContext.RequestServices
                            .GetRequiredService<ISettingsService>();
                        var secret = await settings.GetAsync(SettingKeys.JwtSecret);
                        if (!string.IsNullOrEmpty(secret))
                        {
                            context.Options.TokenValidationParameters.IssuerSigningKey =
                                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
                        }
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(BlogItDefaults.AdminAuthorizationPolicy, policy =>
            {
                policy.AuthenticationSchemes.Add(BlogItDefaults.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
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
