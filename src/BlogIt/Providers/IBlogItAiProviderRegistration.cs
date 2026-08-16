using Microsoft.Extensions.DependencyInjection;

namespace BlogIt;

/// <summary>
/// Registers an <see cref="Services.IAiService"/> implementation, supplied by a satellite package
/// such as <c>BlogIt.OpenAi</c>, into the host's service collection.
/// </summary>
/// <remarks>
/// Mirrors <see cref="IBlogItStorageProviderRegistration"/> deliberately, but is optional rather
/// than required: the engine has exactly one database and exactly one media store, while a host
/// may legitimately want no AI provider at all. The core package therefore ships no implementation
/// and no reference to any AI SDK; when nothing is registered, the AI endpoints answer with a
/// documented "not configured" problem response instead. See <c>BlogItOptions.UseAiProvider</c>.
/// </remarks>
public interface IBlogItAiProviderRegistration
{
    /// <summary>
    /// Short provider name used in configuration-error messages, for example <c>"OpenAi"</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Adds the provider's <see cref="Services.IAiService"/> and any settings it needs.
    /// </summary>
    /// <remarks>
    /// Register the service itself with <c>TryAdd*</c> so a host that has already substituted its
    /// own <see cref="Services.IAiService"/> keeps it.
    /// </remarks>
    void RegisterServices(IServiceCollection services);
}
