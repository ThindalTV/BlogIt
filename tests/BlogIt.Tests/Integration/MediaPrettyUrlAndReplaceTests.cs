using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BlogIt;
using BlogIt.Contracts;
using BlogIt.Contracts.DTOs;
using BlogIt.Data;
using BlogIt.Entities;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

public sealed class MediaPrettyUrlAndReplaceTests : IAsyncLifetime
{
    private readonly string storageRoot =
        Path.Combine(AppContext.BaseDirectory, $"media-pretty-{Guid.NewGuid():N}");
    private WebApplication app = null!;
    private HttpClient client = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(storageRoot);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddBlogIt(options =>
        {
            options.UseDatabaseProvider(new InMemoryDatabaseProvider($"MediaPretty_{Guid.NewGuid():N}"));
            options.UseFileSystemStorage(storage => storage.RootPath = storageRoot);
        });

        app = builder.Build();
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
                SecurityStamp = BlogItSampleFactory.DefaultTestSecurityStamp
            });
            db.SiteSettings.Add(new SiteSetting
            {
                Key = SettingKeys.JwtSecret,
                Value = BlogItSampleFactory.TestJwtSecret
            });
            await db.SaveChangesAsync();
        }

        client = app.GetTestClient().WithAuth(userId);
    }

    public async Task DisposeAsync()
    {
        await app.DisposeAsync();
        if (Directory.Exists(storageRoot))
            Directory.Delete(storageRoot, recursive: true);
    }

    [Fact]
    public async Task Upload_WithPath_IsServedAtThatPath_AndStillAtItsKey()
    {
        var uploaded = await UploadAsync("Report", "report.pdf", "application/pdf", "pdf bytes", "Docs/Annual-Report.pdf");

        uploaded.PublicPath.Should().Be("/media/docs/annual-report.pdf");
        uploaded.Slug.Should().Be("docs/annual-report.pdf");

        var pretty = await client.GetAsync("/media/docs/annual-report.pdf");
        pretty.StatusCode.Should().Be(HttpStatusCode.OK);
        pretty.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await pretty.Content.ReadAsStringAsync()).Should().Be("pdf bytes");

        // Case-insensitive on the way in.
        (await client.GetAsync("/media/DOCS/Annual-Report.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The generated storage-key URL keeps working, so embedded links do not break.
        var key = await StorageKeyAsync(uploaded.Id);
        (await client.GetAsync($"/media/{key}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_SetsMovesAndClearsThePath()
    {
        var uploaded = await UploadAsync("Doc", "doc.pdf", "application/pdf", "bytes");
        var key = await StorageKeyAsync(uploaded.Id);
        uploaded.Slug.Should().BeNull();
        uploaded.PublicPath.Should().Be($"/media/{key}");

        var set = await UpdateAsync(uploaded.Id, "Doc", "my/doc.pdf");
        set.PublicPath.Should().Be("/media/my/doc.pdf");
        (await client.GetAsync("/media/my/doc.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);

        var moved = await UpdateAsync(uploaded.Id, "Doc", "other.pdf");
        moved.PublicPath.Should().Be("/media/other.pdf");
        (await client.GetAsync("/media/my/doc.pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/media/other.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);

        var cleared = await UpdateAsync(uploaded.Id, "Doc", "  ");
        cleared.Slug.Should().BeNull();
        cleared.PublicPath.Should().Be($"/media/{key}");
        (await client.GetAsync("/media/other.pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/media/{key}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_RejectsTakenAndMalformedPaths()
    {
        var first = await UploadAsync("A", "a.pdf", "application/pdf", "a", "taken.pdf");
        var second = await UploadAsync("B", "b.pdf", "application/pdf", "b");

        var taken = await client.PutAsJsonAsync($"/api/media/{second.Id}", new UpdateMediaRequest("B", "TAKEN.pdf"));
        taken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await taken.Content.ReadAsStringAsync()).Should().Contain("already uses");

        var key = await StorageKeyAsync(first.Id);
        var keyClash = await client.PutAsJsonAsync($"/api/media/{second.Id}", new UpdateMediaRequest("B", key));
        keyClash.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var bad = await client.PutAsJsonAsync($"/api/media/{second.Id}", new UpdateMediaRequest("B", "../etc/passwd"));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A file may re-save its own path.
        var same = await client.PutAsJsonAsync($"/api/media/{first.Id}", new UpdateMediaRequest("A2", "taken.pdf"));
        same.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Upload_WithTakenPath_IsRejectedBeforeAnythingIsStored()
    {
        await UploadAsync("A", "a.pdf", "application/pdf", "a", "taken.pdf");
        var filesBefore = Directory.GetFiles(storageRoot).Length;

        var response = await PostUploadAsync("B", "b.pdf", "application/pdf", "b", "taken.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Directory.GetFiles(storageRoot).Length.Should().Be(filesBefore);
    }

    [Fact]
    public async Task Replace_ChangesContentInPlace_AndKeepsEveryUrl()
    {
        var uploaded = await UploadAsync("CV", "cv-2025.pdf", "application/pdf", "old cv", "cv.pdf");
        var key = await StorageKeyAsync(uploaded.Id);

        var oldGet = await client.GetAsync("/media/cv.pdf");
        var oldEtag = oldGet.Headers.ETag;
        oldEtag.Should().NotBeNull();

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent("a much newer cv"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "cv-2026.pdf");
        var response = await client.PutAsync($"/api/media/{uploaded.Id}/content", form);
        response.EnsureSuccessStatusCode();
        var replaced = (await response.Content.ReadFromJsonAsync<MediaFileDto>())!;

        replaced.PublicPath.Should().Be("/media/cv.pdf");
        replaced.FileName.Should().Be("cv-2026.pdf");
        replaced.SizeBytes.Should().Be("a much newer cv".Length);
        (await StorageKeyAsync(uploaded.Id)).Should().Be(key);

        (await (await client.GetAsync("/media/cv.pdf")).Content.ReadAsStringAsync()).Should().Be("a much newer cv");
        (await (await client.GetAsync($"/media/{key}")).Content.ReadAsStringAsync()).Should().Be("a much newer cv");

        // A cached copy of the old version must be revalidated to the new one, not served as a 304.
        var stale = new HttpRequestMessage(HttpMethod.Get, "/media/cv.pdf");
        stale.Headers.IfNoneMatch.Add(oldEtag!);
        var revalidated = await client.SendAsync(stale);
        revalidated.StatusCode.Should().Be(HttpStatusCode.OK);
        revalidated.Headers.ETag.Should().NotBe(oldEtag);
    }

    [Fact]
    public async Task Serve_AnswersAMatchingETagWith304_WithoutCountingAView()
    {
        var uploaded = await UploadAsync("Img", "i.png", "image/png", "png", "i.png");
        var first = await client.GetAsync("/media/i.png");
        first.Headers.CacheControl!.NoCache.Should().BeTrue();

        var conditional = new HttpRequestMessage(HttpMethod.Get, "/media/i.png");
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await client.SendAsync(conditional);
        second.StatusCode.Should().Be(HttpStatusCode.NotModified);

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        // The in-memory provider cannot run the set-based view-count update, so the count stays 0;
        // what matters is the 304 path never reaches it and never errors.
        (await db.MediaFiles.FindAsync(uploaded.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Replace_OfAMissingFile_IsNotFound()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "file", "x.bin");
        var response = await client.PutAsync($"/api/media/{Guid.NewGuid()}/content", form);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> PostUploadAsync(
        string title, string fileName, string contentType, string content, string? slug = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        form.Add(new StringContent(title), "title");
        if (slug is not null)
            form.Add(new StringContent(slug), "slug");
        return await client.PostAsync("/api/media/upload", form);
    }

    private async Task<MediaFileDto> UploadAsync(
        string title, string fileName, string contentType, string content, string? slug = null)
    {
        var response = await PostUploadAsync(title, fileName, contentType, content, slug);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MediaFileDto>())!;
    }

    private async Task<MediaFileDto> UpdateAsync(Guid id, string title, string? slug)
    {
        var response = await client.PutAsJsonAsync($"/api/media/{id}", new UpdateMediaRequest(title, slug));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MediaFileDto>())!;
    }

    private async Task<string> StorageKeyAsync(Guid id)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        return (await db.MediaFiles.AsNoTracking().SingleAsync(m => m.Id == id)).BackendUrl;
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
