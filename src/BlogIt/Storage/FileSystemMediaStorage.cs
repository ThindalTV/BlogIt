using Microsoft.Extensions.Hosting;

namespace BlogIt;

internal sealed class FileSystemMediaStorage : IBlogItMediaStorage
{
    private readonly string rootPath;

    public FileSystemMediaStorage(
        FileSystemStorageSettings settings,
        IHostEnvironment hostEnvironment)
    {
        var configuredPath = settings.RootPath;
        rootPath = Path.GetFullPath(
            Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(hostEnvironment.ContentRootPath, configuredPath));
        Directory.CreateDirectory(rootPath);
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

        var storageKey = CreateStorageKey(originalFileName);
        var destinationPath = GetStoragePath(storageKey);
        var temporaryPath = Path.Combine(rootPath, $".{storageKey}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await content.CopyToAsync(destination, cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationPath, overwrite: false);
            return storageKey;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetStoragePath(storageKey);
        if (!File.Exists(path))
            return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = GetStoragePath(storageKey);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string GetStoragePath(string storageKey)
    {
        ValidateStorageKey(storageKey);
        return Path.Combine(rootPath, storageKey);
    }

    private static void ValidateStorageKey(string storageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);
        if (storageKey is "." or ".."
            || storageKey != Path.GetFileName(storageKey)
            || storageKey.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new ArgumentException(
                "The BlogIt media storage key must be a single file-name segment.",
                nameof(storageKey));
        }
    }

    private static string CreateStorageKey(string originalFileName)
    {
        var extension = Path.GetExtension(Path.GetFileName(originalFileName));
        if (extension.Length is <= 1 or > 17
            || extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            extension = string.Empty;
        }

        return $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
    }
}
