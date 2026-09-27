# BlogIt.AzureStorage

`BlogIt.AzureStorage` adds Azure Blob Storage media to the BlogIt ASP.NET Core
engine. It depends transitively on the same package version of `BlogIt`; do not
install a separate `BlogIt` version alongside it.

## Requirements and install

Use .NET 10 preview, SQL Server, and an Azure Storage account.

```powershell
dotnet add package BlogIt.AzureStorage
```

## Azure storage startup

```csharp
using BlogIt;

var builder = WebApplication.CreateBuilder(args);

var sqlConnection = builder.Configuration.GetConnectionString("BlogItDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItDb is required.");
var storageConnection = builder.Configuration.GetConnectionString("BlogItStorage")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItStorage is required.");

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(sqlConnection);
    options.UseAzureStorage(storage =>
    {
        storage.ConnectionString = storageConnection;
        storage.ContainerName = "blogit-media";
    });
});

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseBlogIt();

app.MapBlogIt();
app.Run();
```

`ConnectionString` is required. `ContainerName` defaults to `blogit-media` and
must satisfy Azure container naming rules. The provider creates the private
container on first use.

BlogIt keeps public media behind its `/media` route by default. The blob name
returned by the provider is an opaque storage key, not a public URL; consumers
must not parse or construct it. Configure BlogIt's `/blogit`, `/api`, and
`/media` prefixes through `BlogItOptions` in the same `AddBlogIt` callback.

`UseBlogIt` adds BlogIt's redirect middleware, and adds authentication,
authorization and rate limiting unless it can see the host already did — it skips
the two auth middlewares when they were registered before it, and
`UseBlogIt(options => …)` opts out explicitly. BlogIt's `BlogIt.Admin` policy
names its `BlogIt.Jwt` scheme, so BlogIt's tokens authenticate for BlogIt's
endpoints regardless of the host's default scheme. See "If your application
already has authentication" in the technical guide.

Map package endpoints with `MapBlogIt`; host-defined public views can use
`IPublicContentService`, `SeoHead`, and `GaScript` from the transitive main
package.
