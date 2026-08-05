using BlogIt;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(
        "Server=localhost;Database=CompileOnly;User ID=compile;Password=Compile_only_2026!;TrustServerCertificate=True");
    options.UseAzureStorage(storage =>
    {
        storage.ConnectionString =
            "DefaultEndpointsProtocol=https;AccountName=compileonly;AccountKey=Y29tcGlsZS1vbmx5;EndpointSuffix=core.windows.net";
        storage.ContainerName = "blogit-media";
    });
});

var app = builder.Build();
app.UseBlogIt();
app.MapBlogIt();
