# BlogIt technical guide

BlogIt is embedded in an ASP.NET Core host. The package owns persistence,
management APIs, authentication, migrations, media delivery, feeds, sitemap and
robots endpoints, and the Blazor admin application. The host owns the public
site's routes and design.

## Requirements

- .NET 10 SDK
- SQL Server
- A writable local directory for filesystem media, or an Azure Blob Storage
  account

Install the main package for filesystem storage:

```powershell
dotnet add package BlogIt
```

Install the Azure provider instead when media belongs in Blob Storage. It
includes the matching `BlogIt` package transitively:

```powershell
dotnet add package BlogIt.AzureStorage
```

## Optional satellite packages

The engine carries no AI or analytics SDK. Both are reached through provider
abstractions in `BlogIt`, and each has its own package that brings the matching
`BlogIt` transitively — install neither, either, or both:

| Package | Adds | Configure with |
| --- | --- | --- |
| `BlogIt.AzureStorage` | Azure Blob media storage | `options.UseAzureStorage(...)` |
| `BlogIt.OpenAi` | The admin's AI brainstorm and export-to-draft screens | `options.UseOpenAi()` |
| `BlogIt.GoogleAnalytics` | The admin dashboard's analytics panel | `options.UseGoogleAnalytics()` |

```powershell
dotnet add package BlogIt.OpenAi
dotnet add package BlogIt.GoogleAnalytics --prerelease
```

`BlogIt.GoogleAnalytics` is prerelease-only because Google publishes no stable
Analytics Data client; see `docs/publishing.md`. Keeping it in a satellite is why
`BlogIt` itself can release stable.

`UseOpenAi()` and `UseGoogleAnalytics()` take no arguments. Both providers read
their credentials, endpoints, and model names from the per-site settings entered
in the admin portal, so there is nothing to configure at startup.

### Without them

Leaving a satellite out is a supported deployment, not a broken one:

| Left out | Effect |
| --- | --- |
| `BlogIt.OpenAi` | `POST /api/ai/conversations/{id}/messages` and `.../export-draft` return `400` with a problem response naming the package to install. Listing, reading, creating, and deleting conversations keep working — they touch only the database. |
| `BlogIt.GoogleAnalytics` | `GET /api/analytics/summary` returns `404 "Analytics is not configured."` — the same answer as an installed provider with no credentials entered. The client-side measurement tag is unaffected: `GaScript` lives in `BlogIt` and needs no SDK. |

Both are also replaceable rather than merely omittable: `IAiService` and
`IAnalyticsService` are public in `BlogIt`, and a host implementation registered
before `AddBlogIt` wins over both the satellite and the fallback. The sample does
this for analytics.

## Configure the host

Add a SQL Server connection string and, optionally, a media root to the host's
configuration:

```json
{
  "ConnectionStrings": {
    "BlogItDb": "Server=localhost;Database=BlogIt;Trusted_Connection=True;TrustServerCertificate=True"
  },
  "BlogIt": {
    "Storage": {
      "RootPath": "App_Data/blogit-media"
    }
  }
}
```

Register BlogIt once, select exactly one database provider and one storage
provider plus at most one AI and one analytics provider, migrate after building
the app, and add BlogIt's middleware and endpoints:

```csharp
using BlogIt;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("BlogItDb")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:BlogItDb is required.");
var mediaRoot =
    builder.Configuration["BlogIt:Storage:RootPath"]
    ?? Path.Combine("App_Data", "blogit-media");

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(connectionString);
    options.UseFileSystemStorage(storage => storage.RootPath = mediaRoot);
});
builder.Services.AddRazorComponents();

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseBlogIt();
app.UseAntiforgery();

app.MapBlogIt();
app.MapRazorComponents<App>();
app.Run();
```

A relative filesystem root resolves against the host's content root. Ensure the
application identity can create, read, and delete files there.

For Azure Blob Storage, replace the filesystem registration:

```csharp
builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(connectionString);
    options.UseAzureStorage(storage =>
    {
        storage.ConnectionString =
            builder.Configuration.GetConnectionString("BlogItStorage")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:BlogItStorage is required.");
        storage.ContainerName = "blogit-media";
    });
});
```

The Azure provider creates its private container on first use. Media keys are
provider-owned values; do not interpret them as paths or public URLs.

## Paths and middleware

The defaults are:

| Option | Default | Purpose |
| --- | --- | --- |
| `AdminPath` | `/blogit` | Packaged admin portal |
| `ApiPath` | `/api` | Setup and authenticated management APIs |
| `MediaPath` | `/media` | Public media proxy |

Override paths in the same registration callback:

```csharp
builder.Services.AddBlogIt(options =>
{
    options.AdminPath = "/admin";
    options.ApiPath = "/blog-api";
    options.MediaPath = "/content";
    options.UseSqlServer(connectionString);
    options.UseFileSystemStorage(storage => storage.RootPath = mediaRoot);
});
```

The three paths must be distinct. BlogIt also claims four fixed root URLs —
`/rss.xml`, `/atom.xml`, `/sitemap.xml`, `/robots.txt` — which are switched off
individually rather than moved; see
[Feeds, sitemap, and robots.txt](#feeds-sitemap-and-robotstxt).

Place forwarding, exception handling, HTTPS,
and static files before `UseBlogIt`. Place host antiforgery after it, then call
`MapBlogIt`. BlogIt registers and invokes its own `BlogIt.Jwt` authentication
scheme and `BlogIt.Admin` policy; the host must not add duplicate authentication
or authorization middleware specifically for BlogIt.

`UseBlogIt` serves the packaged admin portal from the private
`BlogItAdminAssets` folder next to the host assembly. Those assets ship
uncompressed: the package deliberately contains no `.br`/`.gz` variants, because
the admin tree is served through a plain static-file pipeline that performs no
`Accept-Encoding` negotiation, so precompressed copies would only have inflated
every consuming project's `bin/` and `publish/`. The admin payload is a Blazor
WebAssembly application and compresses well, so hosts that care about first-load
transfer size should add `UseResponseCompression` before `UseBlogIt`, or let the
reverse proxy or CDN in front of the application compress and cache the
responses.

`MigrateBlogItAsync` applies the package's EF Core migrations. Run it before the
application begins serving requests and use a database identity with schema
change permission during deployment.

## The data model is part of the public API — on purpose

`BlogItDbContext` and the entity types in `BlogIt.Shared.Entities` are public,
with ordinary settable properties. This is deliberate, not an oversight.

A host can supply its own database provider by registering a
`IBlogItDatabaseProviderRegistration` that calls
`AddDbContextFactory<BlogItDbContext>(...)` — which is exactly what
`options.UseSqlServer(...)` does internally, and what the reference sample does
for its in-memory testing provider. That extension point only works if the
context and the model it maps are visible to the host, so both stay public.

The trade-off that buys: the schema is part of this package's compatibility
surface, and host code holding an entity can write to it directly, bypassing the
rules the API layer enforces. Two consequences worth knowing:

- **Treat the entities as read-mostly.** Go through the API or the services for
  anything that has rules attached — slug generation and locking, publication
  scheduling, password hashing, tag resolution. Setting `IsPublished = true`
  without a `PublishedAt` produces a post no public query will return, because
  "published" means both.
- **A schema change is a breaking change.** Column widths in particular are load
  bearing: `UrlRedirect.SourcePath` is capped at 450 characters
  (`RedirectLimits.SourcePathLength`) because it carries a unique index and SQL
  Server limits a nonclustered key to 1700 bytes. The SEO columns are capped by
  `SeoLimits`, matched by server-side validation.

If you want the blog's tables isolated from the rest of your schema, give BlogIt
its own database or schema rather than reaching for the entities.

## Editing content: concurrency tokens

`BlogPostDetailDto` and `PageDto` carry a `ConcurrencyStamp`. `PUT /posts/{id}`
and `PUT /pages/{id}` require it, and **fail closed**: an omitted or stale value
is rejected with `409 Conflict` rather than overwriting whatever the record now
contains.

The flow is read, edit, send the stamp back:

```csharp
var post = await GetPostAsync(id);                 // carries ConcurrencyStamp
var request = new UpdateBlogPostRequest(
    title, summary, content, seoTitle, seoDescription, seoKeywords, ogImageUrl,
    tagNames, scheduledPublishAt, scheduledUnpublishAt, slug,
    post.ConcurrencyStamp);                        // <- prove it is current
```

Every mutating response returns the new stamp, so a client that keeps the latest
one can save repeatedly without reloading. On a `409`, re-read the record and let
the user decide what to keep — do not retry with the same stamp.

## Build a public site

Inject `BlogIt.Services.IPublicContentService` into a Razor component, page,
controller, or endpoint. It exposes:

| Method | Result |
| --- | --- |
| `GetRecentPostsAsync(count)` | Most recently published posts |
| `GetPostsAsync(page, pageSize)` | Paginated published archive |
| `SearchPostsAsync(query)` | Published posts matching title, summary, or content |
| `GetPostsByTagAsync(slug, page, pageSize)` | Paginated posts for a tag |
| `GetPostAsync(slug, includeNavigation)` | One published post and optional adjacent posts |
| `GetPageAsync(slug)` | One published custom page |

Every method is published-only. "Published" means `IsPublished` is set *and*
`PublishedAt` has a value, so a post scheduled for a future date is excluded
too. Drafts return `null` rather than the content.

The single exception is the `includeUnpublished` parameter on `GetPostAsync`
and `GetPageAsync`, which defaults to `false`. Pass `true` only on a path that
has already authorized a draft preview through `IPreviewTokenService` — the
sample's post page does this for `?preview=<token>` and nowhere else:

```csharp
var content = await Content.GetPostAsync(
    slug,
    includeNavigation: !preview.HasValue,
    includeUnpublished: preview.HasValue);
```

For example, a host-owned archive component can read posts directly:

```razor
@page "/archive"
@inject BlogIt.Services.IPublicContentService Content

@foreach (var post in posts)
{
    <article>
        <h2>
            <a href="@BlogIt.Shared.BlogUrlHelper.GetPostPath(
                post.Slug, post.PublishedAt, post.CreatedAt)">
                @post.Title
            </a>
        </h2>
        <p>@post.Summary</p>
    </article>
}

@code {
    private IReadOnlyList<BlogIt.Shared.DTOs.BlogPostSummaryDto> posts = [];

    protected override async Task OnInitializedAsync()
    {
        posts = (await Content.GetPostsAsync(1, 10)).Posts;
    }
}
```

Post bodies and summaries are Markdown. The host decides how to render and
sanitize them. The sample uses `BlogIt.Helpers.MarkdownHelper.ToHtml` and casts
the result to `MarkupString`.

The package also provides `BlogIt.Components.Shared.SeoHead` and `GaScript`.
Compose them into host pages for metadata, structured data, canonical URLs, and
Google Analytics. `GaScript` emits markup only after a measurement ID is saved
in admin settings.

See `samples/BlogIt.Sample` for archive, post, page, search, tag, preview, SEO,
and analytics examples.

## Feeds, sitemap, and robots.txt

BlogIt maps four documents at the site root. Set the site URL and description in
the admin portal so their absolute URLs and metadata are correct.

| Route | Endpoint name | Switch |
| --- | --- | --- |
| `GET /rss.xml` | `BlogIt.RssFeed` | `ServeRssFeed` |
| `GET /atom.xml` | `BlogIt.AtomFeed` | `ServeAtomFeed` |
| `GET /sitemap.xml` | `BlogIt.Sitemap` | `ServeSitemap` |
| `GET /robots.txt` | `BlogIt.RobotsTxt` | `ServeRobotsTxt` |

Unlike `AdminPath`/`ApiPath`/`MediaPath` these are not configurable paths —
they are conventional URLs a site either owns or does not. Each one is instead
an on/off switch, defaulting to on:

```csharp
builder.Services.AddBlogIt(options =>
{
    // This site already ships its own robots.txt and a combined sitemap.
    options.ServeRobotsTxt = false;
    options.ServeSitemap = false;
    options.UseSqlServer(connectionString);
    options.UseFileSystemStorage(storage => storage.RootPath = mediaRoot);
});
```

Switching one off unmaps the route entirely. That matters because leaving it
mapped is not neutral: a host static file at the same path is silently shadowed
by BlogIt's endpoint, and a host *endpoint* at the same path fails at request
time with `AmbiguousMatchException`. Turning the switch off makes the path the
host's again.

Turning `ServeSitemap` off also drops the `Sitemap:` line from BlogIt's
`robots.txt`, since there is then no such document to point crawlers at.

### Getting the entries as data

Turning a document off must not lose its contents, so
`BlogIt.Services.ISiteMetadataService` exposes all of it as structured data,
alongside `IPublicContentService`:

| Method | Result |
| --- | --- |
| `GetFeedAsync(maxItems)` | `BlogFeed` — channel title, description, site URL, and `BlogFeedItem` entries |
| `GetSitemapEntriesAsync()` | `SitemapEntry` per crawlable URL: site-relative `Path`, absolute `Location`, `LastModified` |
| `GetRobotsDirectivesAsync()` | `RobotsDirectives` — `User-agent` groups and `Sitemap:` URLs |

Everything is published-only, with no `includeUnpublished` escape hatch: these
documents are crawler-facing by definition.

Merging BlogIt's URLs into a host-owned sitemap:

```csharp
app.MapGet("/sitemap.xml", async (BlogIt.Services.ISiteMetadataService metadata) =>
{
    var urls = ownUrls.Concat(
        (await metadata.GetSitemapEntriesAsync())
            .Select(entry => (entry.Location, entry.LastModified)));
    return Results.Content(RenderCombinedSitemap(urls), "application/xml");
});
```

`SitemapEntry.Location` is the site URL and the entry path concatenated, so it
keeps the prefix of a blog mounted at `https://example.com/blog/`. Feed items
deliberately carry only the site-relative `Path`, with the resolved base URL on
`BlogFeed.SiteUrl`; combine them as `feed.SiteUrl.TrimEnd('/') + item.Path`.

If you want BlogIt's exact rendering as well as its data — for example to serve
the same feed from a different route — the renderers are public and take the
data types directly: `FeedService.CreateRss(feed)`, `FeedService.CreateAtom(feed)`,
`SitemapApi.RenderSitemap(entries)`, and `SitemapApi.RenderRobots(directives)`.
The built-in endpoints are these same two steps, so a host-rebuilt document is
byte-for-byte the one BlogIt would have served.

Endpoint names are prefixed (`BlogIt.RssFeed`, not `RssFeed`) — see
`BlogItEndpointNames`. Endpoint names are a flat namespace shared with the host
and a duplicate throws at startup, so the unqualified names stay yours.

## Run and test this repository

Start the Aspire sample, which provisions SQL Server and injects the connection:

```powershell
dotnet run --project .\samples\BlogIt.Sample.AppHost\BlogIt.Sample.AppHost.csproj
```

Build and run the automated suite:

```powershell
dotnet build .\BlogIt.slnx -c Release
dotnet test .\tests\BlogIt.Tests\BlogIt.Tests.csproj -c Release --no-build
```

Package verification lives under `build/package-layout-tests` and
`build/package-smoke`; it validates package contents and clean consumer
applications used by release CI.
