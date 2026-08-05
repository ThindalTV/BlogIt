using BlogIt;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var databaseName = $"PackageConsumer_{Guid.NewGuid():N}";

builder.Services.AddBlogIt(options =>
{
    options.AdminPath = builder.Configuration["AdminPath"] ?? BlogItDefaults.AdminPath;
    options.ApiPath = builder.Configuration["ApiPath"] ?? BlogItDefaults.ApiPath;
    options.UseDatabaseProvider(new ConsumerDatabaseProvider(databaseName));
    options.UseFileSystemStorage(storage =>
        storage.RootPath = Path.Combine(AppContext.BaseDirectory, "media"));
});

var app = builder.Build();

app.UseBlogIt();
app.MapBlogIt();
app.MapGet(
    "/contract-assembly",
    () => typeof(BlogItAdminBootstrapConfig).Assembly.GetName().Name);
app.MapGet(
    "/package-surface",
    () => new[]
    {
        typeof(BlogIt.Components.Shared.SeoHead).FullName,
        typeof(BlogIt.Services.IPublicContentService).FullName,
        typeof(BlogIt.Shared.DTOs.BlogPostSummaryDto).FullName
    });

app.Run();

internal sealed record ConsumerDatabaseProvider(string DatabaseName)
    : IBlogItDatabaseProviderRegistration
{
    public string Name => "PackageProofInMemory";

    public void RegisterServices(IServiceCollection services) =>
        services.AddDbContextFactory<BlogItDbContext>(
            options => options.UseInMemoryDatabase(DatabaseName));
}
