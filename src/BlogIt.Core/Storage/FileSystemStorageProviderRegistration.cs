using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogIt;

internal sealed class FileSystemStorageProviderRegistration(string rootPath)
    : IBlogItStorageProviderRegistration
{
    public string Name => "FileSystem";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton(new FileSystemStorageSettings(rootPath));
        services.TryAddSingleton<IBlogItMediaStorage, FileSystemMediaStorage>();
    }
}

internal sealed record FileSystemStorageSettings(string RootPath);
