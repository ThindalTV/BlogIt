# BlogIt

BlogIt is an embeddable ASP.NET Core blog engine with SQL Server persistence, a
packaged administration application and APIs, filesystem media storage, and
services for host-defined public views.

## Requirements and install

BlogIt targets .NET 10 preview and requires SQL Server.

```powershell
dotnet add package BlogIt
```

Use `BlogIt.AzureStorage` instead when media should be stored in Azure Blob
Storage; that provider brings in the matching BlogIt package transitively.

## Filesystem startup

```csharp
using BlogIt;

var builder = WebApplication.CreateBuilder(args);

var sqlConnection = builder.Configuration.GetConnectionString("BlogItDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItDb is required.");

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(sqlConnection);
    options.UseFileSystemStorage(storage =>
        storage.RootPath = Path.Combine("App_Data", "blogit"));
});
```

Hosting on Azure SQL Database instead of a dedicated SQL Server? Use `UseAzureSql(...)` in place of
`UseSqlServer(...)` — same provider, but with EF Core's connection-retry strategy turned on by
default, since Azure SQL is more prone to transient faults (throttling, failover, elastic pool
moves). With retries enabled, any multi-step write that must be atomic needs to go through
`BlogItDbContext.ExecuteInTransactionAsync(...)` instead of a bare `Database.BeginTransactionAsync()`
— see that method's doc comment for why.

```csharp

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseBlogIt();

app.MapBlogIt();
app.Run();
```

The default prefixes are `/blogit` for the admin application, `/api` for APIs,
and `/media` for public media. Set `AdminPath`, `ApiPath`, or `MediaPath` in the
single `AddBlogIt` callback to change them. Relative filesystem roots resolve
against the host content root.

`AddBlogIt` registers the package-owned `BlogIt.Jwt` scheme and `BlogIt.Admin`
policy. Do not separately add authentication or authorization middleware for
BlogIt. Put forwarding, errors, HTTPS, and static files before `UseBlogIt`;
place host antiforgery after it, then call `MapBlogIt`.

## Public views and providers

Public pages remain host-defined. Inject
`BlogIt.Services.IPublicContentService` into host views and compose the packaged
`BlogIt.Components.Shared.SeoHead` and `GaScript` Razor components as needed.

BlogIt requires exactly one database and one storage provider. Custom
integrations can use `UseDatabaseProvider` and `UseStorageProvider` with
implementations of their public registration interfaces. Media storage keys
are opaque provider values: do not parse them as paths or URLs.

This package contains the server, browser-safe contracts, private admin assets,
and build-transitive asset wiring. There are no separate `BlogIt.Contracts` or
`BlogIt.Admin` packages.
