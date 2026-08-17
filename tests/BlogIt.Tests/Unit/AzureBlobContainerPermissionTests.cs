using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BlogIt;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Pins the container-provisioning behaviour of the Azure media provider against a credential that
/// can read and write blobs but cannot create containers — the shape of a least-privilege SAS or a
/// Storage Blob Data Contributor role assignment scoped to an existing container.
/// </summary>
/// <remarks>
/// Proved with a <see cref="BlobContainerClient"/> subclass rather than a live account: the Azure SDK
/// makes its operations virtual precisely so they can be substituted, so a fake that answers
/// <c>CreateIfNotExistsAsync</c> with the 403 the service returns reproduces the failure exactly,
/// deterministically, and without credentials. The alternative — an Azurite container or a real
/// storage account — cannot even express "blob access granted, container creation denied" without
/// provisioning a scoped credential per test run.
/// </remarks>
public sealed class AzureBlobContainerPermissionTests
{
    private const string StorageKey = "0123456789abcdef0123456789abcdef.png";

    [Fact]
    public async Task OpenReadAsync_NeverAttemptsContainerCreation()
    {
        var container = new FakeContainerClient(RequestFailedFactory.Forbidden);
        var storage = new AzureBlobMediaStorage(container);

        // A missing blob is the fake's answer, which is also the answer a missing container gives:
        // either way this degrades to "no such media", not to a 403 on every image on the site.
        (await storage.OpenReadAsync(StorageKey)).Should().BeNull();

        container.CreateAttempts.Should().Be(0);
        container.Blob.Operations.Should().Equal("download");
    }

    [Fact]
    public async Task DeleteAsync_NeverAttemptsContainerCreation()
    {
        var container = new FakeContainerClient(RequestFailedFactory.Forbidden);
        var storage = new AzureBlobMediaStorage(container);

        await storage.DeleteAsync(StorageKey);

        container.CreateAttempts.Should().Be(0);
        container.Blob.Operations.Should().Equal("delete");
    }

    [Fact]
    public async Task StoreAsync_WhenContainerCreationIsForbidden_UploadsAnywayAndStopsRetryingTheCreate()
    {
        var container = new FakeContainerClient(RequestFailedFactory.Forbidden);
        var storage = new AzureBlobMediaStorage(container);

        await StoreAsync(storage);
        await StoreAsync(storage);

        // One attempt total, not one per upload: a permission failure is permanent, so retrying it
        // only adds a doomed round trip to every write.
        container.CreateAttempts.Should().Be(1);
        container.Blob.Operations.Should().Equal("upload", "upload");
    }

    [Fact]
    public async Task StoreAsync_WhenContainerCreationFailsTransiently_RetriesOnTheNextWrite()
    {
        var container = new FakeContainerClient(RequestFailedFactory.ServiceUnavailable);
        var storage = new AzureBlobMediaStorage(container);

        var first = () => StoreAsync(storage);
        await first.Should().ThrowAsync<RequestFailedException>();
        var second = () => StoreAsync(storage);
        await second.Should().ThrowAsync<RequestFailedException>();

        // The opposite of the forbidden case on purpose: a 503 says "ask again later", so caching it
        // as "container handled" would strand the provider on a container that was never created.
        container.CreateAttempts.Should().Be(2);
        container.Blob.Operations.Should().BeEmpty();
    }

    private static async Task StoreAsync(AzureBlobMediaStorage storage)
    {
        await using var content = new MemoryStream("bytes"u8.ToArray());
        await storage.StoreAsync(content, "hero.png", "image/png");
    }

    private static class RequestFailedFactory
    {
        public static RequestFailedException Forbidden() => new(
            403, "This request is not authorized to perform this operation.", "AuthorizationFailure", null);

        public static RequestFailedException ServiceUnavailable() => new(
            503, "The server is busy.", "ServerBusy", null);
    }

    private sealed class FakeContainerClient(Func<RequestFailedException> createFailure)
        : BlobContainerClient
    {
        public int CreateAttempts { get; private set; }

        public FakeBlobClient Blob { get; } = new();

        public override Task<Response<BlobContainerInfo>> CreateIfNotExistsAsync(
            PublicAccessType publicAccessType = PublicAccessType.None,
            IDictionary<string, string>? metadata = null,
            BlobContainerEncryptionScopeOptions? encryptionScopeOptions = null,
            CancellationToken cancellationToken = default)
        {
            CreateAttempts++;
            return Task.FromException<Response<BlobContainerInfo>>(createFailure());
        }

        public override BlobClient GetBlobClient(string blobName) => Blob;
    }

    private sealed class FakeBlobClient : BlobClient
    {
        private readonly List<string> operations = [];

        public IReadOnlyList<string> Operations => operations;

        public override Task<Response<BlobContentInfo>> UploadAsync(
            Stream content,
            BlobUploadOptions options,
            CancellationToken cancellationToken = default)
        {
            operations.Add("upload");
            return Task.FromResult(Response.FromValue(
                BlobsModelFactory.BlobContentInfo(
                    new ETag("\"fake\""), DateTimeOffset.UtcNow, null, null, 0),
                new FakeResponse()));
        }

        public override Task<Response<BlobDownloadStreamingResult>> DownloadStreamingAsync(
            BlobDownloadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            operations.Add("download");
            // 404 is what the service answers for a missing blob and for a blob in a container that
            // does not exist, so one fake covers both of the cases the read path has to survive.
            return Task.FromException<Response<BlobDownloadStreamingResult>>(
                new RequestFailedException(404, "The specified blob does not exist.", "BlobNotFound", null));
        }

        public override Task<Response<bool>> DeleteIfExistsAsync(
            DeleteSnapshotsOption snapshotsOption = DeleteSnapshotsOption.None,
            BlobRequestConditions? conditions = null,
            CancellationToken cancellationToken = default)
        {
            operations.Add("delete");
            return Task.FromResult(Response.FromValue(false, new FakeResponse()));
        }
    }

    /// <summary>The minimum <see cref="Response"/> the SDK's Response.FromValue needs.</summary>
    private sealed class FakeResponse : Response
    {
        public override int Status => 200;

        public override string ReasonPhrase => "OK";

        public override Stream? ContentStream { get; set; }

        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose()
        {
        }

        protected override bool ContainsHeader(string name) => false;

        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];

        // Signatures match the base exactly (non-nullable out, never read on a false return) so the
        // Release build stays warning-free.
        protected override bool TryGetHeader(string name, out string value)
        {
            value = null!;
            return false;
        }

        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = null!;
            return false;
        }
    }
}
