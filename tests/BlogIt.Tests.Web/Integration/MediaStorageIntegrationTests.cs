using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlogIt;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

public sealed class MediaStorageIntegrationTests
{
    [Fact]
    public async Task MediaEndpoints_RoundTripOpaqueFileSystemKeyAtConfiguredPath()
    {
        var storageRoot = Path.Combine(
            AppContext.BaseDirectory,
            $"media-integration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(storageRoot);

        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddBlogIt(options =>
            {
                options.MediaPath = "/assets";
                options.UseDatabaseProvider(
                    new InMemoryDatabaseProvider($"MediaIntegration_{Guid.NewGuid():N}"));
                options.UseFileSystemStorage(storage => storage.RootPath = storageRoot);
            });

            await using var app = builder.Build();
            await app.MigrateBlogItAsync();
            app.UseBlogIt();
            app.MapBlogIt();
            await app.StartAsync();

            var userId = Guid.NewGuid();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
                db.Users.Add(new AppUser
                {
                    Id = userId,
                    Username = "media-user",
                    DisplayName = "Media User",
                    PasswordHash = "unused",
                    // Must match the stamp WithAuth signs into the token, or authentication
                    // rejects it as revoked.
                    SecurityStamp = BlogItSampleFactory.DefaultTestSecurityStamp
                });
                db.SiteSettings.Add(new SiteSetting
                {
                    Key = SettingKeys.JwtSecret,
                    Value = BlogItSampleFactory.TestJwtSecret
                });
                await db.SaveChangesAsync();
            }

            var client = app.GetTestClient().WithAuth(userId);
            var bytes = "stored image bytes"u8.ToArray();
            using var form = new MultipartFormDataContent();
            using var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "Hero Image.PNG");
            form.Add(new StringContent("Hero"), "title");

            var uploadResponse = await client.PostAsync("/api/media/upload", form);
            uploadResponse.EnsureSuccessStatusCode();
            var uploaded = await uploadResponse.Content.ReadFromJsonAsync<MediaFileDto>();

            uploaded.Should().NotBeNull();
            uploaded!.PublicPath.Should().MatchRegex("^/assets/[a-f0-9]{32}\\.png$");

            string storageKey;
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
                var entity = await db.MediaFiles.SingleAsync();
                storageKey = entity.BackendUrl;
                storageKey.Should().NotBe(entity.PublicPath);
                entity.PublicPath.Should().Be(uploaded.PublicPath);
            }
            File.Exists(Path.Combine(storageRoot, storageKey)).Should().BeTrue();

            var downloadResponse = await client.GetAsync(uploaded.PublicPath);
            downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            downloadResponse.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            downloadResponse.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
            (await downloadResponse.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

            var deleteResponse = await client.DeleteAsync($"/api/media/{uploaded.Id}");
            deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
            File.Exists(Path.Combine(storageRoot, storageKey)).Should().BeFalse();
            (await client.GetAsync(uploaded.PublicPath)).StatusCode
                .Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            if (Directory.Exists(storageRoot))
                Directory.Delete(storageRoot, recursive: true);
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
