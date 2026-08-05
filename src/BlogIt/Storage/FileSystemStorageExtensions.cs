namespace BlogIt;

public static class FileSystemStorageExtensions
{
    public static BlogItOptions UseFileSystemStorage(
        this BlogItOptions options,
        Action<FileSystemStorageOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var storageOptions = new FileSystemStorageOptions();
        configure?.Invoke(storageOptions);

        if (string.IsNullOrWhiteSpace(storageOptions.RootPath))
        {
            throw new InvalidOperationException(
                "BlogIt filesystem storage RootPath must not be empty.");
        }

        return options.UseStorageProvider(
            new FileSystemStorageProviderRegistration(storageOptions.RootPath.Trim()));
    }
}
