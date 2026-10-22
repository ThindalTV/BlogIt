namespace BlogIt;

public interface IBlogItMediaStorage
{
    Task<string> StoreAsync(
        Stream content,
        string originalFileName,
        string contentType,
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenReadAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the object stored under <paramref name="storageKey"/> with new content, keeping
    /// the key — and so every URL that points at it — unchanged.
    /// </summary>
    /// <remarks>
    /// A default member so storage providers written against the earlier interface keep compiling;
    /// replacing a file through the admin API reports "not supported" for such a provider.
    /// </remarks>
    Task ReplaceAsync(
        string storageKey,
        Stream content,
        string contentType,
        CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException(
            $"{GetType().Name} does not support replacing stored media."));
}
