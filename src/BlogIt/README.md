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

### Optional satellite packages

This package carries no AI or analytics SDK — install a satellite only if you
want that feature, and each brings the matching `BlogIt` transitively:

| Package | Adds | Configure with |
| --- | --- | --- |
| `BlogIt.AzureStorage` | Azure Blob media storage | `options.UseAzureStorage(...)` |
| `BlogIt.OpenAi` | The admin's AI brainstorm and export-to-draft screens | `options.UseOpenAi()` |
| `BlogIt.GoogleAnalytics` | The admin dashboard's analytics panel | `options.UseGoogleAnalytics()` |

Without `BlogIt.OpenAi`, the two AI endpoints that call a provider answer `400`
naming the package to install; the conversation list and CRUD still work. Without
`BlogIt.GoogleAnalytics`, the analytics summary answers `404 "Analytics is not
configured."` — the same response as an installed provider with no credentials
entered. `GaScript`, the client-side measurement tag, is in this package and
needs no satellite.

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

BlogIt requires exactly one database and one storage provider, and accepts at
most one AI and one analytics provider. Custom integrations can use
`UseDatabaseProvider`, `UseStorageProvider`, `UseAiProvider`, and
`UseAnalyticsProvider` with implementations of their public registration
interfaces. Media storage keys are opaque provider values: do not parse them as
paths or URLs.

AI and analytics are optional because a host may legitimately want neither; with
no provider the engine registers documented not-configured services rather than
leaving `IAiService` and `IAnalyticsService` unresolvable. Registering your own
`IAiService` or `IAnalyticsService` before `AddBlogIt` wins over both a satellite
package and those fallbacks.

This package contains the server, browser-safe contracts, private admin assets,
and build-transitive asset wiring. There are no separate `BlogIt.Contracts` or
`BlogIt.Admin` packages.
