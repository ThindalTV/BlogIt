using BlogIt.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogIt;

internal sealed class GoogleAnalyticsProviderRegistration : IBlogItAnalyticsProviderRegistration
{
    public string Name => "GoogleAnalytics";

    public void RegisterServices(IServiceCollection services)
    {
        // Scoped to match the scoped ISettingsService it reads credentials through. TryAdd so a
        // host that registered its own IAnalyticsService before AddBlogIt keeps it, and so that
        // this - registered ahead of the core package's NotConfiguredAnalyticsService fallback - is
        // what wins.
        services.TryAddScoped<IAnalyticsService, GoogleAnalyticsService>();
    }
}
