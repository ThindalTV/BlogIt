using BlogIt;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BlogIt.Tests.Unit;

public sealed class FileSystemMediaStorageTests : IDisposable
{
    private readonly string rootPath =
        Path.Combine(AppContext.BaseDirectory, $"BlogItStorageTests-{Guid.NewGuid():N}");
    private readonly List<ServiceProvider> serviceProviders = [];

    [Fact]
    public async Task StoreOpenAndDelete_RoundTripsContent()
    {
        var storage = CreateStorage();
        await using var content = new MemoryStream("stored content"u8.ToArray());

        var key = await storage.StoreAsync(content, "My Photo.PNG", "image/png");

        key.Should().MatchRegex("^[a-f0-9]{32}\\.png$");
        await using (var stored = await storage.OpenReadAsync(key))
        {
            stored.Should().NotBeNull();
            using var reader = new StreamReader(stored!);
            (await reader.ReadToEndAsync()).Should().Be("stored content");
        }

        await storage.DeleteAsync(key);
        (await storage.OpenReadAsync(key)).Should().BeNull();
    }

    [Fact]
    public async Task ReplaceAsync_OverwritesContentUnderTheSameKey_EvenWhileItIsBeingRead()
    {
        var storage = CreateStorage();
        await using var original = new MemoryStream("old content"u8.ToArray());
        var key = await storage.StoreAsync(original, "doc.pdf", "application/pdf");

        // An open reader must not block the replace (on Windows it would without FileShare.Delete)
        // and must keep seeing the content it opened.
        await using var reader = await storage.OpenReadAsync(key);
        await using var replacement = new MemoryStream("new content"u8.ToArray());
        await storage.ReplaceAsync(key, replacement, "application/pdf");

        using (var oldReader = new StreamReader(reader!))
            (await oldReader.ReadToEndAsync()).Should().Be("old content");
        await using var after = await storage.OpenReadAsync(key);
        using var newReader = new StreamReader(after!);
        (await newReader.ReadToEndAsync()).Should().Be("new content");
        Directory.EnumerateFiles(rootPath).Should().ContainSingle();
    }

    [Fact]
    public async Task ReplaceAsync_WhenSourceFails_KeepsThePreviousContent()
    {
        var storage = CreateStorage();
        await using var original = new MemoryStream("old content"u8.ToArray());
        var key = await storage.StoreAsync(original, "doc.pdf", "application/pdf");
        await using var failing = new FailingCopyStream();

        var replace = () => storage.ReplaceAsync(key, failing, "application/pdf");

        await replace.Should().ThrowAsync<IOException>();
        await using var after = await storage.OpenReadAsync(key);
        using var reader = new StreamReader(after!);
        (await reader.ReadToEndAsync()).Should().Be("old content");
    }

    [Fact]
    public async Task StoreAsync_WhenSourceFails_LeavesNoPartialOrTemporaryFile()
    {
        var storage = CreateStorage();
        await using var content = new FailingCopyStream();

        var store = () => storage.StoreAsync(content, "partial.png", "image/png");

        await store.Should().ThrowAsync<IOException>()
            .WithMessage("Simulated source failure.");
        Directory.EnumerateFiles(rootPath).Should().BeEmpty();
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("folder/file.txt")]
    [InlineData("folder\\file.txt")]
    public async Task StorageOperations_RejectPathTraversal(string key)
    {
        var storage = CreateStorage();

        var read = () => storage.OpenReadAsync(key);
        var delete = () => storage.DeleteAsync(key);

        await read.Should().ThrowAsync<ArgumentException>();
        await delete.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void UseFileSystemStorage_RegistersProviderAndStorage()
    {
        var provider = CreateServiceProvider();

        provider.GetRequiredService<IBlogItMediaStorage>()
            .Should().NotBeNull();
    }

    public void Dispose()
    {
        foreach (var provider in serviceProviders)
            provider.Dispose();
        if (Directory.Exists(rootPath))
            Directory.Delete(rootPath, recursive: true);
    }

    private IBlogItMediaStorage CreateStorage() =>
        CreateServiceProvider().GetRequiredService<IBlogItMediaStorage>();

    private ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(AppContext.BaseDirectory));
        services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseFileSystemStorage(storage => storage.RootPath = rootPath);
        });
        var provider = services.BuildServiceProvider();
        serviceProviders.Add(provider);
        return provider;
    }

    private sealed class FakeDatabaseProvider : IBlogItDatabaseProviderRegistration
    {
        public string Name => "Fake";
        public void RegisterServices(IServiceCollection services)
        {
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "BlogIt.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class FailingCopyStream : MemoryStream
    {
        public override async Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync("partial content"u8.ToArray(), cancellationToken);
            throw new IOException("Simulated source failure.");
        }
    }
}
