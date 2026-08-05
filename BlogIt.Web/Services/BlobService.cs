using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BlogIt.Web.Services;

public class BlobService(IConfiguration configuration) : IBlobService
{
    private async Task<BlobContainerClient> GetContainerAsync()
    {
        var connectionString = configuration.GetConnectionString("BlogItStorage")
            ?? throw new InvalidOperationException("Azure Storage connection string is not configured.");
        var containerName = configuration["BlogStorage:ContainerName"] ?? "blogit-media";

        var client = new BlobContainerClient(connectionString, containerName);
        await client.CreateIfNotExistsAsync(PublicAccessType.None);
        return client;
    }

    public async Task<(string BackendUrl, string PublicPath)> UploadAsync(
        Stream data, string fileName, string contentType)
    {
        var container = await GetContainerAsync();
        var blobName = $"{Guid.NewGuid():N}-{SanitizeFileName(fileName)}";
        var blob = container.GetBlobClient(blobName);

        await blob.UploadAsync(data, new BlobHttpHeaders { ContentType = contentType });

        return (blob.Uri.ToString(), $"/media/{blobName}");
    }

    public async Task DeleteAsync(string backendUrl)
    {
        var container = await GetContainerAsync();
        var uri = new Uri(backendUrl);
        var blobName = uri.Segments[^1];
        var blob = container.GetBlobClient(blobName);
        await blob.DeleteIfExistsAsync();
    }

    public async Task<Stream?> DownloadAsync(string fileName)
    {
        var container = await GetContainerAsync();
        var blob = container.GetBlobClient(fileName);

        if (!await blob.ExistsAsync())
            return null;

        var response = await blob.DownloadStreamingAsync();
        return response.Value.Content;
    }

    private static string SanitizeFileName(string fileName)
    {
        return string.Join("-", fileName.Split(Path.GetInvalidFileNameChars()))
            .ToLowerInvariant();
    }
}
