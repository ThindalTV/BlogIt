# BlogIt.GoogleAnalytics

`BlogIt.GoogleAnalytics` adds Google Analytics reporting to the admin dashboard of the
BlogIt ASP.NET Core engine. It depends transitively on the same package version of
`BlogIt`; do not install a separate `BlogIt` version alongside it.

Install this only if you want the dashboard's analytics panel. Without it, `BlogIt`
carries no reference to the Google Analytics SDK — and therefore none of the
Gax/gRPC/Protobuf tree beneath it — and the analytics endpoint reports that analytics is
not configured.

## Prerelease only

This package ships as a prerelease and will keep doing so until Google publishes a stable
Google Analytics Data client. The Data API surface is `v1beta`, so a stable `1.0.0` here
would raise `NU5104` and be rejected by feeds that block prerelease transitives.

Isolating that in a satellite is the point: `BlogIt` and the other satellites release
stable from the same commit, and only hosts that actually want analytics opt into a
prerelease dependency.

```powershell
dotnet add package BlogIt.GoogleAnalytics --prerelease
```

## Requirements

Use .NET 10, SQL Server, a GA4 property, and a Google service account with the Analytics
Data API enabled and viewer access to that property.

## Analytics startup

```csharp
using BlogIt;

var builder = WebApplication.CreateBuilder(args);

var sqlConnection = builder.Configuration.GetConnectionString("BlogItDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItDb is required.");

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(sqlConnection);
    options.UseFileSystemStorage();
    options.UseGoogleAnalytics();
});

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseBlogIt();

app.MapBlogIt();
app.Run();
```

`UseGoogleAnalytics()` takes no arguments on purpose. The GA4 property ID and the
service-account JSON are stored per site and entered in the admin's Settings screen, so
there is nothing to configure at startup and nothing that can be configured in two
places. The service-account JSON is held as a secret setting and is never returned by the
settings API.

## Reporting, not measurement

This package only reads reports. The client-side measurement tag is separate: the
`GaScript` component in the core `BlogIt` package emits it from the saved measurement ID
and needs neither this package nor any SDK. A site can therefore collect analytics without
installing this at all — it just will not show them on the dashboard.

## Replacing the provider

`IAnalyticsService` is a public abstraction in `BlogIt`. Register your own implementation
before `AddBlogIt` and it wins over both this package and the engine's not-configured
fallback.

## Not installing this package

`GET /api/analytics/summary` answers `404 "Analytics is not configured."` — byte-identical
to what a site gets with this package installed but its property ID or credentials left
blank, so the dashboard panel handles both without change.
