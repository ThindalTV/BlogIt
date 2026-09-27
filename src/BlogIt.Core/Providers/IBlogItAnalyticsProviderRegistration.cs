using Microsoft.Extensions.DependencyInjection;

namespace BlogIt;

/// <summary>
/// Registers an <see cref="Services.IAnalyticsService"/> implementation, supplied by a satellite
/// package such as <c>BlogIt.GoogleAnalytics</c>, into the host's service collection.
/// </summary>
/// <remarks>
/// Optional in the same way as <see cref="IBlogItAiProviderRegistration"/>: with no provider
/// registered the analytics summary endpoint reports "not configured", which is the same answer it
/// already gave for a provider whose credentials were never filled in. See
/// <c>BlogItOptions.UseAnalyticsProvider</c>.
/// </remarks>
public interface IBlogItAnalyticsProviderRegistration
{
    /// <summary>
    /// Short provider name used in configuration-error messages, for example
    /// <c>"GoogleAnalytics"</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Adds the provider's <see cref="Services.IAnalyticsService"/> and any settings it needs.
    /// </summary>
    /// <remarks>
    /// Register the service itself with <c>TryAdd*</c> so a host that has already substituted its
    /// own <see cref="Services.IAnalyticsService"/> keeps it.
    /// </remarks>
    void RegisterServices(IServiceCollection services);
}
