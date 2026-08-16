using BlogIt.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogIt;

internal sealed class OpenAiProviderRegistration : IBlogItAiProviderRegistration
{
    public string Name => "OpenAi";

    public void RegisterServices(IServiceCollection services)
    {
        // Scoped because OpenAiService takes the scoped BlogItDbContext. TryAdd so a host that
        // registered its own IAiService before AddBlogIt keeps it, and so that this - registered
        // ahead of the core package's NotConfiguredAiService fallback - is what wins.
        services.TryAddScoped<IAiService, OpenAiService>();
    }
}
