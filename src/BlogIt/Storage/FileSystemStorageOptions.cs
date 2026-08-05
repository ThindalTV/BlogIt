namespace BlogIt;

public sealed class FileSystemStorageOptions
{
    public string RootPath { get; set; } = Path.Combine("App_Data", "blogit");
}
