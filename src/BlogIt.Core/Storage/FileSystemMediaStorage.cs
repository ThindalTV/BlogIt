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
        await WriteAsync(storageKey, content, overwrite: false, cancellationToken);
        return storageKey;
    }

    public async Task ReplaceAsync(
        string storageKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        await WriteAsync(storageKey, content, overwrite: true, cancellationToken);
    }

    /// <summary>
    /// Writes to a temporary file and moves it into place, so a reader never sees a half-written
    /// object and a failed write leaves the previous content untouched.
    /// </summary>
    private async Task WriteAsync(
        string storageKey,
        Stream content,
        bool overwrite,
        CancellationToken cancellationToken)
    {
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

            if (overwrite && File.Exists(destinationPath))
            {
                // File.Replace rather than a Move that overwrites: on Windows the latter is refused
                // while any reader has the target open, even with delete sharing, and a download in
                // progress is exactly when an editor tends to replace a file. Replace swaps it out
                // from under the reader, who carries on with the old content.
                File.Replace(temporaryPath, destinationPath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, destinationPath, overwrite: false);
            }
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
            // Delete as well as Read: on Windows a file open without FILE_SHARE_DELETE cannot be
            // replaced or removed, so one slow download would make ReplaceAsync fail.
            FileShare.Read | FileShare.Delete,
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
        // Both separators are rejected on every platform, rather than whichever ones the running
        // platform happens to use. On Unix, Path.DirectorySeparatorChar and its alt are both '/',
        // so a key containing a backslash passed this check and Path.GetFileName left it untouched
        // — the guard silently did nothing on the platform most deployments run on. It would also
        // have written a file whose name becomes a traversal the moment the same store is read on
        // Windows.
        if (storageKey is "." or ".."
            || storageKey != Path.GetFileName(storageKey)
            || storageKey.IndexOfAny(['/', '\\']) >= 0)
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
