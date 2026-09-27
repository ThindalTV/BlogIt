using BlogIt;
using BlogIt.Shared.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Unit;

public class AzureSqlOptionsTests
{
    // Never actually connects — CreateExecutionStrategy() only inspects configured options.
    private const string FakeConnectionString =
        "Server=tcp:fake.database.windows.net,1433;Database=fake;User ID=fake;Password=fake;Encrypt=True;";

    [Fact]
    public async Task UseAzureSql_EnablesRetryingExecutionStrategy()
    {
        await using var db = await BuildContextAsync(options => options.UseAzureSql(FakeConnectionString));

        db.Database.CreateExecutionStrategy().GetType().Name.Should().Contain("Retrying");
    }

    [Fact]
    public async Task UseSqlServer_DoesNotEnableRetryingExecutionStrategyByDefault()
    {
        await using var db = await BuildContextAsync(options => options.UseSqlServer(FakeConnectionString));

        db.Database.CreateExecutionStrategy().GetType().Name.Should().NotContain("Retrying");
    }

    private static async Task<BlogItDbContext> BuildContextAsync(Action<BlogItOptions> configureDatabase)
    {
        var services = new ServiceCollection();
        services.AddBlogIt(options =>
        {
            configureDatabase(options);
            options.UseStorageProvider(new NullStorageProvider());
        });

        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<BlogItDbContext>>();
        return await factory.CreateDbContextAsync();
    }

    private sealed class NullStorageProvider : IBlogItStorageProviderRegistration
    {
        public string Name => "null-storage";

        public void RegisterServices(IServiceCollection services) =>
            services.AddSingleton<IBlogItMediaStorage, NullMediaStorage>();
    }

    private sealed class NullMediaStorage : IBlogItMediaStorage
    {
        public Task<string> StoreAsync(
            Stream source, string originalFileName, string contentType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
