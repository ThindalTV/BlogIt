using System.Collections.Concurrent;
using BlogIt;
using BlogIt.Shared.Data;
using Microsoft.EntityFrameworkCore;

internal sealed class TestingDatabaseProviderRegistration(string databaseName)
    : IBlogItDatabaseProviderRegistration
{
    public string Name => "testing-in-memory";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddDbContextFactory<BlogItDbContext>(
            options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IBlogItMigrator, TestingBlogItMigrator>();
    }
}

internal sealed class TestingBlogItMigrator(
    IDbContextFactory<BlogItDbContext> dbContextFactory) : IBlogItMigrator
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.EnsureCreatedAsync(cancellationToken);
    }
}

internal sealed class TestingStorageProviderRegistration : IBlogItStorageProviderRegistration
{
    public string Name => "testing-memory";

    public void RegisterServices(IServiceCollection services) =>
        services.AddSingleton<IBlogItMediaStorage, TestingMediaStorage>();
}

internal sealed class TestingMediaStorage : IBlogItMediaStorage
{
    private readonly ConcurrentDictionary<string, byte[]> content = new();

    public async Task<string> StoreAsync(
        Stream source,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var key = Guid.NewGuid().ToString("N");
        await using var destination = new MemoryStream();
        await source.CopyToAsync(destination, cancellationToken);
        content[key] = destination.ToArray();
        return key;
    }

    public Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream? stream = content.TryGetValue(storageKey, out var value)
            ? new MemoryStream(value, writable: false)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        content.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }
}
