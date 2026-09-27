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
}
