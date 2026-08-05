using Microsoft.Extensions.DependencyInjection;

namespace BlogIt;

public interface IBlogItDatabaseProviderRegistration
{
    string Name { get; }

    void RegisterServices(IServiceCollection services);
}
