using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogIt;

internal sealed class AzureStorageProviderRegistration(
    string connectionString,
    string containerName)
    : IBlogItStorageProviderRegistration
{
    public string Name => "AzureStorage";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(
            new AzureStorageSettings(connectionString, containerName));
        services.TryAddSingleton<IBlogItMediaStorage, AzureBlobMediaStorage>();
    }
}

internal sealed record AzureStorageSettings(
    string ConnectionString,
    string ContainerName);
