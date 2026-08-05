using BlogIt;
using BlogIt.Services;
using BlogIt.Shared.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

public class PackageOptionsApiTests
{
    [Fact]
    public void Options_HaveStableDefaults()
    {
        var options = new BlogItOptions();

        options.AdminPath.Should().Be("/blogit");
        options.ApiPath.Should().Be("/api");
        options.MediaPath.Should().Be("/media");
        BlogItDefaults.AuthenticationScheme.Should().Be("BlogIt.Jwt");
    }

    [Fact]
    public void AddBlogIt_NormalizesPathsAndRegistersProviders()
    {
        var services = new ServiceCollection();

        services.AddBlogIt(options =>
        {
            options.AdminPath = " blogit/admin/ ";
            options.ApiPath = "//v1//api//";
            options.MediaPath = "media";
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseStorageProvider(new FakeStorageProvider());
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BlogItOptions>>().Value;

        options.AdminPath.Should().Be("/blogit/admin");
        options.ApiPath.Should().Be("/v1/api");
        options.MediaPath.Should().Be("/media");
        provider.GetRequiredService<IBlogItDatabaseProviderRegistration>().Name.Should().Be("fake-db");
        provider.GetRequiredService<IBlogItStorageProviderRegistration>().Name.Should().Be("fake-storage");
        provider.GetRequiredService<DatabaseService>().Should().NotBeNull();
        provider.GetRequiredService<StorageService>().Should().NotBeNull();
    }

    [Fact]
    public void AddBlogIt_RegistersEngineServicesNamedSchemeAndAdminPolicy()
    {
        var services = new ServiceCollection();
        services.AddBlogIt(AddFakeProviders);

        using var provider = services.BuildServiceProvider();
        var authentication = provider
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;
        var policy = provider
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value.GetPolicy(BlogItDefaults.AdminAuthorizationPolicy);

        authentication.Schemes
            .Should().ContainSingle(scheme =>
                scheme.Name == BlogItDefaults.AuthenticationScheme);
        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes
            .Should().Equal(BlogItDefaults.AuthenticationScheme);
        policy.Requirements.Should().ContainSingle(
            requirement => requirement is DenyAnonymousAuthorizationRequirement);
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ISettingsService)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IAuthService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(PublicationSchedulingService));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IBlogItMiddlewareContributor));
        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IBlogItEndpointContributor));
    }

    [Fact]
    public void AddBlogIt_PreservesHostAuthenticationAndAuthorizationDefaults()
    {
        const string hostScheme = "Host.Cookie";
        var hostPolicy = new AuthorizationPolicyBuilder(hostScheme)
            .RequireClaim("host-user")
            .Build();
        var services = new ServiceCollection();
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = hostScheme;
                options.DefaultAuthenticateScheme = hostScheme;
                options.DefaultChallengeScheme = hostScheme;
            })
            .AddCookie(hostScheme);
        services.AddAuthorization(options =>
        {
            options.DefaultPolicy = hostPolicy;
            options.FallbackPolicy = hostPolicy;
        });

        services.AddBlogIt(AddFakeProviders);

        using var provider = services.BuildServiceProvider();
        var authentication = provider
            .GetRequiredService<IOptions<AuthenticationOptions>>()
            .Value;
        var authorization = provider
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value;

        authentication.DefaultScheme.Should().Be(hostScheme);
        authentication.DefaultAuthenticateScheme.Should().Be(hostScheme);
        authentication.DefaultChallengeScheme.Should().Be(hostScheme);
        authentication.Schemes.Should().Contain(scheme => scheme.Name == hostScheme);
        authentication.Schemes.Should().ContainSingle(scheme =>
            scheme.Name == BlogItDefaults.AuthenticationScheme);
        authorization.DefaultPolicy.Should().BeSameAs(hostPolicy);
        authorization.FallbackPolicy.Should().BeSameAs(hostPolicy);
        authorization.GetPolicy(BlogItDefaults.AdminAuthorizationPolicy)!
            .AuthenticationSchemes.Should().Equal(BlogItDefaults.AuthenticationScheme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/api?version=1")]
    [InlineData("/api#fragment")]
    [InlineData(@"api\v1")]
    [InlineData("/api/../admin")]
    public void AddBlogIt_RejectsInvalidPaths(string path)
    {
        var services = new ServiceCollection();

        var action = () => services.AddBlogIt(options =>
        {
            options.ApiPath = path;
            AddFakeProviders(options);
        });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*ApiPath*");
    }

    [Fact]
    public void AddBlogIt_RejectsMissingDatabaseProvider()
    {
        var action = () => new ServiceCollection().AddBlogIt(options =>
            options.UseStorageProvider(new FakeStorageProvider()));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one database provider*");
    }

    [Fact]
    public void AddBlogIt_RejectsMissingStorageProvider()
    {
        var action = () => new ServiceCollection().AddBlogIt(options =>
            options.UseDatabaseProvider(new FakeDatabaseProvider()));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one storage provider*");
    }

    [Fact]
    public void AddBlogIt_RejectsDuplicateDatabaseProvider()
    {
        var action = () => new ServiceCollection().AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseDatabaseProvider(new FakeDatabaseProvider("second-db"));
            options.UseStorageProvider(new FakeStorageProvider());
        });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one database provider*already configured*");
    }

    [Fact]
    public void AddBlogIt_RejectsDuplicateStorageProvider()
    {
        var action = () => new ServiceCollection().AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseStorageProvider(new FakeStorageProvider());
            options.UseStorageProvider(new FakeStorageProvider("second-storage"));
        });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly one storage provider*already configured*");
    }

    [Fact]
    public void AddBlogIt_RejectsRepeatedCallsBeforeRunningSecondConfiguration()
    {
        var services = new ServiceCollection();
        services.AddBlogIt(AddFakeProviders);
        var secondConfigurationRan = false;

        var action = () => services.AddBlogIt(options =>
        {
            secondConfigurationRan = true;
            AddFakeProviders(options);
        });

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*already been called*");
        secondConfigurationRan.Should().BeFalse();
    }

    [Fact]
    public async Task ApplicationExtensions_InvokeContributorsInRegistrationOrder()
    {
        var calls = new List<string>();
        var migrationTokens = new List<CancellationToken>();
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddBlogIt(AddFakeProviders);
        builder.Services.AddSingleton<IBlogItMiddlewareContributor>(
            new FakeMiddlewareContributor(calls, "middleware-1"));
        builder.Services.AddSingleton<IBlogItMiddlewareContributor>(
            new FakeMiddlewareContributor(calls, "middleware-2"));
        builder.Services.AddSingleton<IBlogItEndpointContributor>(
            new FakeEndpointContributor(calls));
        builder.Services.AddSingleton<IBlogItMigrator>(
            new FakeMigrator(calls, migrationTokens, "migrator-1"));
        builder.Services.AddSingleton<IBlogItMigrator>(
            new FakeMigrator(calls, migrationTokens, "migrator-2"));

        await using var app = builder.Build();
        using var cancellation = new CancellationTokenSource();

        app.UseBlogIt().Should().BeSameAs(app);
        app.MapBlogIt().Should().BeSameAs(app);
        await app.MigrateBlogItAsync(cancellation.Token);

        calls.Should().Equal(
            "middleware-1",
            "middleware-2",
            "endpoints",
            "migrator-1",
            "migrator-2");
        migrationTokens.Should().Equal(cancellation.Token, cancellation.Token);
    }

    [Fact]
    public async Task ApplicationExtensions_UseEngineContributorsAndRejectMissingMigrator()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddBlogIt(AddFakeProviders);
        await using var app = builder.Build();

        app.UseBlogIt().Should().BeSameAs(app);
        app.MapBlogIt().Should().BeSameAs(app);
        var migrate = async () => await app.MigrateBlogItAsync();

        await migrate.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*IBlogItMigrator*");
    }

    [Fact]
    public async Task UseBlogIt_RejectsDuplicateHostAuthenticationMiddleware()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddBlogIt(AddFakeProviders);
        await using var app = builder.Build();
        app.UseAuthentication();

        var action = () => app.UseBlogIt();

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*middleware was added before UseBlogIt*");
    }

    [Fact]
    public async Task MapBlogIt_UsesConfiguredApiAndMediaPaths()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddBlogIt(options =>
        {
            options.ApiPath = "/management/v2";
            options.MediaPath = "/content";
            options.UseDatabaseProvider(new FakeDatabaseProvider());
            options.UseStorageProvider(new FakeStorageProvider());
        });
        await using var app = builder.Build();

        app.MapBlogIt();

        var endpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
        var patterns = endpoints
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToList();

        patterns.Should().Contain("/management/v2/posts/");
        patterns.Should().Contain("/management/v2/setup/status");
        patterns.Should().Contain("/content/{**path}");
        patterns.Should().Contain("/rss.xml");
        patterns.Should().Contain("/blogit/_blogit/config");
        patterns.Should().Contain("/blogit/{**path:nonfile}");
        patterns.Should().NotContain(pattern =>
            pattern != null && pattern.StartsWith("/api/", StringComparison.Ordinal));

        var postsEndpoint = endpoints.First(endpoint =>
            endpoint.RoutePattern.RawText == "/management/v2/posts/");
        postsEndpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(data =>
                data.Policy == BlogItDefaults.AdminAuthorizationPolicy);
    }

    private static void AddFakeProviders(BlogItOptions options)
    {
        options.UseDatabaseProvider(new FakeDatabaseProvider());
        options.UseStorageProvider(new FakeStorageProvider());
    }

    private sealed record FakeDatabaseProvider(string Name = "fake-db")
        : IBlogItDatabaseProviderRegistration
    {
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<DatabaseService>();
            services.AddDbContextFactory<BlogItDbContext>(options =>
                options.UseInMemoryDatabase($"PackageOptions_{Guid.NewGuid():N}"));
        }
    }

    private sealed record FakeStorageProvider(string Name = "fake-storage")
        : IBlogItStorageProviderRegistration
    {
        public void RegisterServices(IServiceCollection services)
        {
            services.AddSingleton<StorageService>();
            services.AddSingleton<IBlogItMediaStorage>(
                provider => provider.GetRequiredService<StorageService>());
        }
    }

    private sealed class DatabaseService;

    private sealed class StorageService : IBlogItMediaStorage
    {
        public Task<string> StoreAsync(
            Stream content,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Guid.NewGuid().ToString("N"));

        public Task<Stream?> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeMiddlewareContributor(List<string> calls, string value)
        : IBlogItMiddlewareContributor
    {
        public void Configure(IApplicationBuilder application) => calls.Add(value);
    }

    private sealed class FakeEndpointContributor(List<string> calls)
        : IBlogItEndpointContributor
    {
        public void MapEndpoints(IEndpointRouteBuilder endpoints) => calls.Add("endpoints");
    }

    private sealed class FakeMigrator(
        List<string> calls,
        List<CancellationToken> tokens,
        string value) : IBlogItMigrator
    {
        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            calls.Add(value);
            tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
