using BlogIt;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Unit;

public sealed class AzureStorageTests
{
    private const string ConnectionString =
        "DefaultEndpointsProtocol=https;AccountName=blogit;AccountKey=YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXo=;EndpointSuffix=core.windows.net";

    [Fact]
    public void UseAzureStorage_RegistersOneStorageProviderWithFrozenDefaults()
    {
        AzureStorageOptions? configuredStorage = null;
        var services = new ServiceCollection();

        services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseAzureStorage(storage =>
            {
                configuredStorage = storage;
                storage.ConnectionString = ConnectionString;
            });
        });

        configuredStorage!.ConnectionString = "changed";
        configuredStorage.ContainerName = "changed";

        services.Count(descriptor =>
                descriptor.ServiceType == typeof(IBlogItMediaStorage))
            .Should().Be(1);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBlogItMediaStorage>()
            .Should().BeOfType<AzureBlobMediaStorage>();
        provider.GetRequiredService<AzureStorageSettings>().Should().Be(
            new AzureStorageSettings(ConnectionString, "blogit-media"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseAzureStorage_RejectsBlankConnectionString(string? connectionString)
    {
        var configure = () => CreateServices(
            storage => storage.ConnectionString = connectionString);

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionString*empty*");
    }

    [Fact]
    public void UseAzureStorage_RejectsInvalidConnectionString()
    {
        var configure = () => CreateServices(
            storage => storage.ConnectionString = "not a connection string");

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionString*invalid*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("BlogIt-media")]
    [InlineData("-blogit")]
    [InlineData("blogit-")]
    [InlineData("blogit--media")]
    [InlineData("blogit_media")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijkl")]
    public void UseAzureStorage_RejectsInvalidContainerName(string containerName)
    {
        var configure = () => CreateServices(storage =>
        {
            storage.ConnectionString = ConnectionString;
            storage.ContainerName = containerName;
        });

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage("*ContainerName*");
    }

    [Fact]
    public void StorageKeys_AreOpaqueUniqueAndPreserveOnlySafeExtensions()
    {
        var first = AzureBlobMediaStorage.CreateStorageKey("My Photo.PNG");
        var second = AzureBlobMediaStorage.CreateStorageKey("My Photo.PNG");
        var unsafeExtension = AzureBlobMediaStorage.CreateStorageKey("photo.bad-name");

        first.Should().MatchRegex("^[a-f0-9]{32}\\.png$");
        second.Should().MatchRegex("^[a-f0-9]{32}\\.png$");
        second.Should().NotBe(first);
        unsafeExtension.Should().MatchRegex("^[a-f0-9]{32}$");
    }

    private static IServiceCollection CreateServices(
        Action<AzureStorageOptions> configureStorage)
    {
        var services = new ServiceCollection();
        services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseAzureStorage(configureStorage);
        });
        return services;
    }

    private sealed class FakeDatabaseProvider : IBlogItDatabaseProviderRegistration
    {
        public string Name => "Fake";

        public void RegisterServices(IServiceCollection services)
        {
        }
    }
}
