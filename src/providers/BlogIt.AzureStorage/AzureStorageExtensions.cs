using Azure.Storage.Blobs;

namespace BlogIt;

public static class AzureStorageExtensions
{
    public static BlogItOptions UseAzureStorage(
        this BlogItOptions options,
        Action<AzureStorageOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configure);

        var storageOptions = new AzureStorageOptions();
        configure(storageOptions);

        var connectionString = ValidateConnectionString(storageOptions.ConnectionString);
        var containerName = ValidateContainerName(storageOptions.ContainerName);

        try
        {
            _ = new BlobContainerClient(connectionString, containerName);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new InvalidOperationException(
                "BlogIt Azure storage ConnectionString is invalid.",
                exception);
        }

        return options.UseStorageProvider(
            new AzureStorageProviderRegistration(connectionString, containerName));
    }

    private static string ValidateConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "BlogIt Azure storage ConnectionString must not be empty.");
        }

        return connectionString.Trim();
    }

    private static string ValidateContainerName(string? containerName)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException(
                "BlogIt Azure storage ContainerName must not be empty.");
        }

        containerName = containerName.Trim();
        var hasValidLength = containerName.Length is >= 3 and <= 63;
        var hasValidCharacters = containerName.All(
            character => character is >= 'a' and <= 'z'
                or >= '0' and <= '9'
                or '-');
        var hasValidHyphens = containerName[0] != '-'
            && containerName[^1] != '-'
            && !containerName.Contains("--", StringComparison.Ordinal);

        if (!hasValidLength || !hasValidCharacters || !hasValidHyphens)
        {
            throw new InvalidOperationException(
                "BlogIt Azure storage ContainerName must be 3-63 characters, use only lowercase letters, numbers, and single hyphens, and start and end with a letter or number.");
        }

        return containerName;
    }
}
