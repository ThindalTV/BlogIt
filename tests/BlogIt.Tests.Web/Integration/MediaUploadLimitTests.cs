using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BlogIt;
using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogIt.Tests.Integration;

/// <summary>
/// Covers the media upload size limit: that BlogIt owns it, and that exceeding it produces an answer
/// rather than an empty rejection.
/// </summary>
/// <remarks>
/// The upload handler used to bind an <c>IFormFile</c>, and binding reads the body — so an oversize
/// upload was rejected inside model binding, before any BlogIt code ran. The client got a bare 413
/// with no body, which the admin portal could only report as an unexplained failure. Worse, the
/// ceiling was whatever the host's server defaulted to (30 MB on Kestrel) while the portal itself
/// allowed 50 MB, so the two shipped disagreeing.
/// </remarks>
public sealed class MediaUploadLimitTests
{
    [Fact]
    public async Task AnUploadOverTheLimit_IsRejectedWithABodyThatNamesTheLimit()
    {
        await using var host = await StartAsync(maxUploadBytes: 4 * 1024);

        var response = await host.Client.PostAsync("/api/media/upload", Multipart(new byte[16 * 1024]));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);

        // The assertion that matters: a body at all. This was empty before, which is precisely why
        // the portal could not explain the failure.
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrWhiteSpace();

        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("detail").GetString()
            .Should().Contain("MaxMediaUploadBytes");
        problem.RootElement.GetProperty("maxBytes").GetInt64().Should().Be(4 * 1024);
    }

    [Fact]
    public async Task AnUploadUnderTheLimit_Succeeds()
    {
        await using var host = await StartAsync(maxUploadBytes: 64 * 1024);

        var response = await host.Client.PostAsync("/api/media/upload", Multipart(new byte[8 * 1024]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheLimitIsBlogItsOwn_NotWhateverTheServerDefaultedTo()
    {
        // Deliberately above Kestrel's 30 MB default request-body size. Before the endpoint carried
        // its own limit, this upload failed no matter what BlogIt was configured to allow, because
        // the server rejected the body before routing reached the handler.
        await using var host = await StartAsync(maxUploadBytes: 48L * 1024 * 1024);

        var response = await host.Client.PostAsync(
            "/api/media/upload",
            Multipart(new byte[36 * 1024 * 1024]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void AZeroLimit_IsRejectedWhenOptionsAreValidated()
    {
        var services = new ServiceCollection();

        var configure = () => services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new InMemoryDatabaseProvider($"Limit_{Guid.NewGuid():N}"));
            options.UseFileSystemStorage(storage =>
                storage.RootPath = Path.Combine(AppContext.BaseDirectory, "unused"));
            options.MaxMediaUploadBytes = 0;
        });

        configure.Should().Throw<InvalidOperationException>()
            .WithMessage("*MaxMediaUploadBytes*");
    }

    private static MultipartFormDataContent Multipart(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { content, "file", "upload.png" } };
    }

    private static async Task<TestHost> StartAsync(long maxUploadBytes)
    {
        var storageRoot = Path.Combine(AppContext.BaseDirectory, $"media-limit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new InMemoryDatabaseProvider($"MediaLimit_{Guid.NewGuid():N}"));
            options.UseFileSystemStorage(storage => storage.RootPath = storageRoot);
            options.MaxMediaUploadBytes = maxUploadBytes;
        });

        var app = builder.Build();
        app.UseBlogIt();
        app.MapBlogIt();
        await app.MigrateBlogItAsync();

        var uploaderId = await app.InitializeBlogItAsync(new BlogItSetupRequest
        {
            Username = "owner",
            DisplayName = "Owner",
            Password = "Str0ng-Password!",
            SiteName = "Media",
            SiteUrl = "https://example.com"
        })
            ? await SeedTokenAsync(app)
            : throw new InvalidOperationException("Setup should have claimed a fresh site.");

        await app.StartAsync();

        return new TestHost(app, app.GetTestClient().WithAuth(uploaderId, "owner"), storageRoot);
    }

    /// <summary>
    /// Aligns the site's JWT secret with the one the test token helper signs with, and returns the
    /// seeded administrator's id.
    /// </summary>
    private static async Task<Guid> SeedTokenAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        var user = await db.Users.FirstAsync();
        user.SecurityStamp = BlogItSampleFactory.DefaultTestSecurityStamp;
        await db.SaveChangesAsync();

        var settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        await settings.SetAsync(SettingKeys.JwtSecret, BlogItSampleFactory.TestJwtSecret);
        return user.Id;
    }

    private sealed record TestHost(WebApplication App, HttpClient Client, string StorageRoot)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            if (Directory.Exists(StorageRoot))
                Directory.Delete(StorageRoot, recursive: true);
        }
    }

    private sealed class InMemoryDatabaseProvider(string databaseName)
        : IBlogItDatabaseProviderRegistration
    {
        public string Name => "test-in-memory";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddDbContextFactory<BlogItDbContext>(
                options => options.UseInMemoryDatabase(databaseName));
            services.AddScoped(provider =>
                provider.GetRequiredService<IDbContextFactory<BlogItDbContext>>()
                    .CreateDbContext());
            services.AddSingleton<IBlogItMigrator, EnsureCreatedMigrator>();
        }
    }

    private sealed class EnsureCreatedMigrator(
        IDbContextFactory<BlogItDbContext> factory) : IBlogItMigrator
    {
        public async Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }
    }
}
