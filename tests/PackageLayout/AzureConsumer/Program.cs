using BlogIt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(
        "Server=localhost;Database=PackageProof;User Id=package-proof;Password=package-proof;TrustServerCertificate=True");
    options.UseAzureStorage(storage =>
    {
        storage.ConnectionString =
            "DefaultEndpointsProtocol=https;AccountName=blogit;AccountKey=YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXo=;EndpointSuffix=core.windows.net";
        storage.ContainerName = "blogit-media";
    });
});

var app = builder.Build();
app.UseBlogIt();
app.MapBlogIt();
app.Run();
