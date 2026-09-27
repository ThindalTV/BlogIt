using Microsoft.Extensions.DependencyInjection;

namespace BlogIt;

public interface IBlogItStorageProviderRegistration
{
    string Name { get; }

    void RegisterServices(IServiceCollection services);
}
