namespace BlogIt.Web.Services;

public interface IBlobService
{
    Task<(string BackendUrl, string PublicPath)> UploadAsync(Stream data, string fileName, string contentType);
    Task DeleteAsync(string backendUrl);
    Task<Stream?> DownloadAsync(string fileName);
}
