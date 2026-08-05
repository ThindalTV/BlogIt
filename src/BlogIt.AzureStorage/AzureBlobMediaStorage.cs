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
    {
        container = new BlobContainerClient(
            settings.ConnectionString,
            settings.ContainerName);
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
        await EnsureContainerAsync(cancellationToken);

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
        await EnsureContainerAsync(cancellationToken);

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

            await container.CreateIfNotExistsAsync(
                PublicAccessType.None,
                cancellationToken: cancellationToken);
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
