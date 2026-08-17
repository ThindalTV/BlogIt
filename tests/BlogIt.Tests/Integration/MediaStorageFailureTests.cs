using System.Collections.Concurrent;
using System.Net.Http.Headers;
using BlogIt.Shared.Data;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

/// <summary>
/// The media endpoints touch two stores that cannot be committed atomically — the blob and the row.
/// These tests pin the direction each one is allowed to fail in: never a surviving row that points at
/// a deleted object, because that is a permanent 404 with no repair path, whereas an orphaned object
/// is inert and can be swept.
/// </summary>
/// <remarks>
/// The save failure is injected rather than provoked; <see cref="SaveFailureFactory"/> explains why.
/// The storage double records keys instead of using the sample's in-memory one so the test can see
/// whether a compensating delete actually happened, not just whether the object is still readable.
/// </remarks>
public sealed class MediaStorageFailureTests
{
    [Fact]
    public async Task Upload_WhenTheRowCannotBeSaved_RemovesTheObjectItJustStored()
    {
        await using var factory = new RecordingStorageFactory();
        var client = await AuthedClientAsync(factory);

        factory.Failures.NextFailure = SaveFailureSwitch.DuplicateKeyOnRelationalProvider();
        var upload = () => UploadAsync(client);
        await upload.Should().ThrowAsync<Exception>();

        factory.Storage.StoredKeys.Should().HaveCount(1);
        factory.Storage.DeletedKeys.Should().Equal(factory.Storage.StoredKeys);
        factory.Storage.LiveKeys.Should().BeEmpty();
        (await MediaRowCountAsync(factory)).Should().Be(0);
    }

    [Fact]
    public async Task Delete_WhenTheRowCannotBeSaved_LeavesTheObjectInPlaceForTheSurvivingRow()
    {
        await using var factory = new RecordingStorageFactory();
        var client = await AuthedClientAsync(factory);

        var uploaded = await UploadAsync(client);
        uploaded.EnsureSuccessStatusCode();
        var mediaId = await SingleMediaIdAsync(factory);

        factory.Failures.NextFailure = SaveFailureSwitch.DuplicateKeyOnRelationalProvider();
        var delete = () => client.DeleteAsync($"/api/media/{mediaId}");
        await delete.Should().ThrowAsync<Exception>();

        // The row survived, so the object it points at must too — otherwise the media is a 404
        // forever. Retrying the delete is the repair path, and it only works if the object is here.
        (await MediaRowCountAsync(factory)).Should().Be(1);
        factory.Storage.DeletedKeys.Should().BeEmpty();
        factory.Storage.LiveKeys.Should().Equal(factory.Storage.StoredKeys);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent("stored image bytes"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "Hero Image.png");
        form.Add(new StringContent("Hero"), "title");
        return await client.PostAsync("/api/media/upload", form);
    }

    private static async Task<HttpClient> AuthedClientAsync(RecordingStorageFactory factory)
    {
        var userId = await factory.SeedUserAsync("media-failure-user");
        return factory.CreateClient().WithAuth(userId, "media-failure-user");
    }

    private static async Task<int> MediaRowCountAsync(RecordingStorageFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<BlogItDbContext>().MediaFiles.CountAsync();
    }

    private static async Task<Guid> SingleMediaIdAsync(RecordingStorageFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var media = await scope.ServiceProvider
            .GetRequiredService<BlogItDbContext>().MediaFiles.SingleAsync();
        return media.Id;
    }

    private sealed class RecordingStorageFactory : SaveFailureFactory
    {
        public RecordingMediaStorage Storage { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(
                services => services.AddSingleton<IBlogItMediaStorage>(Storage));
        }
    }

    private sealed class RecordingMediaStorage : IBlogItMediaStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> objects = new();
        private readonly List<string> stored = [];
        private readonly List<string> deleted = [];

        public IReadOnlyList<string> StoredKeys => stored;

        public IReadOnlyList<string> DeletedKeys => deleted;

        public IReadOnlyList<string> LiveKeys => objects.Keys.Order().ToList();

        public async Task<string> StoreAsync(
            Stream source,
            string originalFileName,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            var key = $"{Guid.NewGuid():N}.png";
            await using var destination = new MemoryStream();
            await source.CopyToAsync(destination, cancellationToken);
            objects[key] = destination.ToArray();
            lock (stored) stored.Add(key);
            return key;
        }

        public Task<Stream?> OpenReadAsync(
            string storageKey,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(objects.TryGetValue(storageKey, out var value)
                ? new MemoryStream(value, writable: false)
                : null);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            lock (deleted) deleted.Add(storageKey);
            objects.TryRemove(storageKey, out _);
            return Task.CompletedTask;
        }
    }
}
