using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BlogIt;

internal sealed class AzureBlobMediaStorage : IBlogItMediaStorage
{
    private readonly BlobContainerClient container;
    private readonly SemaphoreSlim containerInitializationLock = new(1, 1);
    private bool isContainerInitialized;

    public AzureBlobMediaStorage(AzureStorageSettings settings)
        : this(new BlobContainerClient(settings.ConnectionString, settings.ContainerName))
    {
    }

    /// <summary>
    /// Takes the container client directly so tests can substitute one whose operations fail the way
    /// the service does. The SDK makes those operations virtual for exactly this, and it is the only
    /// way to cover a credential that is denied container creation without provisioning a real
    /// scoped credential against a real storage account.
    /// </summary>
    internal AzureBlobMediaStorage(BlobContainerClient container)
    {
        this.container = container;
    }

    public async Task<string> StoreAsync(
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        await EnsureContainerAsync(cancellationToken);

        var storageKey = CreateStorageKey(originalFileName);
        var blob = container.GetBlobClient(storageKey);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType
                },
                Conditions = new BlobRequestConditions
                {
                    IfNoneMatch = ETag.All
                }
            },
            cancellationToken);

        return storageKey;
    }

    public async Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        ValidateStorageKey(storageKey);

        // Deliberately no EnsureContainerAsync. Reading cannot need a container created for it: if
        // the container is missing there is nothing to read, and the service answers 404 for a blob
        // in a missing container just as it does for a missing blob, so this path degrades to "no
        // such media" on its own. Calling it here instead demanded container-create permission on
        // every image request, and a credential scoped to blob read/write got a 403 on the first one
        // and — because the initialised flag was only set after a successful create — on every
        // request after that. That turns a provisioning nuance into every image on the site being
        // permanently broken.
        try
        {
            var response = await container
                .GetBlobClient(storageKey)
                .DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        ValidateStorageKey(storageKey);

        // No EnsureContainerAsync here either, and for the same reason as OpenReadAsync: deleting a
        // blob from a container that does not exist is already a no-op, which is exactly what
        // DeleteIfExists means.
        await container
            .GetBlobClient(storageKey)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }

    internal static string CreateStorageKey(string originalFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);

        var extension = Path.GetExtension(Path.GetFileName(originalFileName));
        if (extension.Length is <= 1 or > 17
            || extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            extension = string.Empty;
        }

        return $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref isContainerInitialized))
            return;

        await containerInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (isContainerInitialized)
                return;

            try
            {
                await container.CreateIfNotExistsAsync(
                    PublicAccessType.None,
                    cancellationToken: cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.Status is 403 or 409)
            {
                // The credential may read and write blobs but not create containers — a SAS or role
                // assignment scoped to an already-provisioned container, which is the recommended
                // least-privilege setup. Creation is a convenience, not a requirement: swallow the
                // refusal and let the upload below speak for itself. If the container really is
                // missing, that upload fails with its own 404 per request, which is a clear error
                // rather than a blanket 403 on everything.
                //
                // Only these two statuses. A 5xx or a timeout means "ask again", so it must NOT be
                // cached as handled — see the transient case in AzureBlobContainerPermissionTests.
            }

            // Set even when the create was refused above, so the doomed round trip is attempted once
            // per process rather than once per write.
            Volatile.Write(ref isContainerInitialized, true);
        }
        finally
        {
            containerInitializationLock.Release();
        }
    }

    private static void ValidateStorageKey(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var extensionSeparator = storageKey.IndexOf('.');
        var identifier = extensionSeparator < 0
            ? storageKey
            : storageKey[..extensionSeparator];
        var extension = extensionSeparator < 0
            ? string.Empty
            : storageKey[(extensionSeparator + 1)..];

        var isValidIdentifier = identifier.Length == 32
            && identifier.All(character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f');
        var isValidExtension = extensionSeparator < 0
            || extension.Length is >= 1 and <= 16
            && extension.All(character => char.IsAsciiLetterOrDigit(character)
                && !char.IsAsciiLetterUpper(character));

        if (!isValidIdentifier || !isValidExtension)
        {
            throw new ArgumentException(
                "The BlogIt Azure media storage key is invalid.",
                nameof(storageKey));
        }
    }
}
