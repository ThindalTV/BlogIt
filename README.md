# BlogIt

BlogIt is an embeddable ASP.NET Core blog engine. It supplies SQL Server
persistence, management APIs, a packaged Blazor administration application,
media storage, migrations, and services that a host can use to render its own
public site.

BlogIt has two public packages:

| Package | Use it for |
| --- | --- |
| `BlogIt` | The engine, SQL Server provider, filesystem media provider, admin application, contracts, and public-site helpers. |
| `BlogIt.AzureStorage` | The Azure Blob Storage media provider. It brings in the same-version `BlogIt` package transitively. |

`BlogIt.Contracts` and `BlogIt.Admin` are implementation projects included in
the main package's output; they are not standalone packages.

## Prerequisites

- .NET 10 preview SDK
- SQL Server reachable by the host
- PowerShell 7 for the packaging verification scripts
- An Azure Storage account only when using `BlogIt.AzureStorage`

## Install

For SQL Server with filesystem media:

```powershell
dotnet add package BlogIt
```

For SQL Server with Azure Blob Storage, install the provider package (the main
package is transitive):

```powershell
dotnet add package BlogIt.AzureStorage
```

## Startup

### Filesystem storage

The configuration names below are host conventions; BlogIt receives their
values through `AddBlogIt`.

```csharp
using BlogIt;

var builder = WebApplication.CreateBuilder(args);

var sqlConnection = builder.Configuration.GetConnectionString("BlogItDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItDb is required.");
var mediaRoot = builder.Configuration["BlogIt:Storage:RootPath"]
    ?? Path.Combine("App_Data", "blogit");

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(sqlConnection);
    options.UseFileSystemStorage(storage => storage.RootPath = mediaRoot);
});

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseBlogIt();

app.MapBlogIt();
app.Run();
```

A relative filesystem `RootPath` is resolved against the host content root.

### Azure Blob Storage

```csharp
using BlogIt;

var builder = WebApplication.CreateBuilder(args);

var sqlConnection = builder.Configuration.GetConnectionString("BlogItDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItDb is required.");
var storageConnection = builder.Configuration.GetConnectionString("BlogItStorage")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItStorage is required.");
var containerName =
    builder.Configuration["BlogIt:Storage:ContainerName"] ?? "blogit-media";

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(sqlConnection);
    options.UseAzureStorage(storage =>
    {
        storage.ConnectionString = storageConnection;
        storage.ContainerName = containerName;
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

The Azure provider creates the private container when it first needs it.

## Paths, authentication, and middleware

`BlogItOptions` exposes three distinct URL prefixes:

| Option | Default | Purpose |
| --- | --- | --- |
| `AdminPath` | `/blogit` | Packaged administration application |
| `ApiPath` | `/api` | Management and content API endpoints |
| `MediaPath` | `/media` | Public media proxy |

Set these properties inside the single `AddBlogIt` callback. BlogIt normalizes
leading and trailing separators and rejects empty, unsafe, or duplicate paths.
The filesystem provider defaults to `App_Data/blogit`; the Azure provider
requires `ConnectionString` and defaults `ContainerName` to `blogit-media`.

`AddBlogIt` owns the `BlogIt.Jwt` authentication scheme and the authenticated
`BlogIt.Admin` authorization policy. First-run setup creates the signing secret
stored in BlogIt's settings. Do not call `UseAuthentication` or
`UseAuthorization` separately for BlogIt. Place forwarding, error handling,
HTTPS, and static-file middleware before `UseBlogIt`; it adds redirect,
authentication, and authorization middleware in that order. Place antiforgery
middleware, when used by the host, after `UseBlogIt`, then map endpoints with
`MapBlogIt`. Run `MigrateBlogItAsync` after building the host and before serving
requests.

## Host-defined public site

BlogIt deliberately does not own the public site's pages or visual design.
Host Razor components or endpoints can inject
`BlogIt.Services.IPublicContentService` to query published posts, tags, search
results, and pages. The main package also includes
`BlogIt.Components.Shared.SeoHead` and `GaScript`; hosts can compose them into
their own views. `GaScript` emits tracking markup only when the corresponding
BlogIt setting is configured.

The sample demonstrates this model in `samples/BlogIt.Sample`, including its
host-owned Razor views.

## Run the Aspire sample

The AppHost provisions SQL Server and injects the `BlogItDb` connection while
the sample stores media under `samples/BlogIt.Sample/App_Data/blogit-media`.

```powershell
dotnet run --project .\samples\BlogIt.Sample.AppHost\BlogIt.Sample.AppHost.csproj
```

Open the HTTPS endpoint shown by Aspire. The admin application is at
`/blogit/`; first use opens setup. Start through the AppHost rather than running
the web project directly so that SQL configuration is supplied.

## Providers and extension points

Exactly one database provider and one storage provider must be selected in
`AddBlogIt`. In addition to `UseSqlServer`, `UseFileSystemStorage`, and
`UseAzureStorage`, integrations can implement
`IBlogItDatabaseProviderRegistration` or `IBlogItStorageProviderRegistration`
and pass them to `UseDatabaseProvider` or `UseStorageProvider`. Database
providers register an `IBlogItMigrator`; storage providers register
`IBlogItMediaStorage`. Advanced integrations can also contribute pipeline and
route behavior through `IBlogItMiddlewareContributor` and
`IBlogItEndpointContributor`.

Treat the string returned by `IBlogItMediaStorage.StoreAsync` as an opaque,
provider-owned key. BlogIt persists it and passes it back unchanged to
`OpenReadAsync` and `DeleteAsync`; consumers must not parse it as a filesystem
path, blob URL, or stable public URL. Public media is served through
`MediaPath`.

## Build and validate

Build and run all tests:

```powershell
dotnet build .\BlogIt.slnx -c Release
dotnet test .\BlogIt.Tests\BlogIt.Tests.csproj -c Release --no-build
```

Run the fast package proof. It packs both products, inspects their normal and
symbol packages, restores clean consumers, and exercises default and custom
paths:

```powershell
.\packaging-spike\verify.ps1
```

For the full installed-package SQL and media smoke on Windows with LocalDB:

```powershell
$version = "0.0.1-local"
$database = "BlogItPackageSmoke_$([Guid]::NewGuid().ToString('N'))"
$connection = "Server=(localdb)\MSSQLLocalDB;Database=$database;Integrated Security=true;MultipleActiveResultSets=true;TrustServerCertificate=true"

.\packaging-spike\verify.ps1 -PackageVersion $version
.\package-smoke\run.ps1 `
    -PackageVersion $version `
    -PackageFeed .\packaging-spike\artifacts\feed `
    -ConnectionString $connection `
    -OutputPath .\artifacts\package-smoke
```

Use an equivalent fresh SQL Server database connection when LocalDB is not
available. The reusable `Installed package smoke` workflow offers the same full
proof manually.

## Release

See [Publishing](docs/publishing.md) for the publication preflight and feed
settings. A pushed `vMAJOR.MINOR.PATCH` tag triggers the tag-only release
workflow. It runs the reusable SQL package smoke, verifies checksums, and
publishes only those validated `.nupkg` and `.snupkg` files:

```powershell
git tag v1.2.3
git push <configured-remote-name> v1.2.3
```

Configure repository variable `NUGET_SOURCE` and secret `NUGET_API_KEY`.
Optional settings are `NUGET_SYMBOL_SOURCE`, `NUGET_SYMBOL_API_KEY`, and
`NUGET_USERNAME` for a separate symbol endpoint or authenticated feed.

No license has been selected in this repository. Selecting and adding an
authorized license is the one owner decision that must be completed before a
public NuGet release.
