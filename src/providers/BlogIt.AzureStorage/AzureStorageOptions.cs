namespace BlogIt;

public sealed class AzureStorageOptions
{
    public string? ConnectionString { get; set; }

    public string ContainerName { get; set; } = "blogit-media";
}
