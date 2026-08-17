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

### Where the AI provider may be reached

The AI base URL is a per-site setting, so anyone with blog admin credentials can
change it, and the configured API key is sent to whatever it names. **By default
BlogIt refuses a base URL on a loopback, link-local, or private address** — an
absolute `http(s)` URL is required, and `http://169.254.169.254/`,
`http://localhost:11434/v1`, `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`,
`100.64.0.0/10`, IPv6 unique-local and link-local, and names ending in
`.localhost`, `.local`, `.internal` or `.home.arpa` are all rejected. The check
runs when the setting is saved and again when the client is built, so a value
stored before this existed is caught too.

If you run your own model on the machine or the private network, allow it in host
startup:

```csharp
builder.Services.AddBlogIt(options =>
{
    options.AllowPrivateAiEndpoints = true; // e.g. Ollama on http://localhost:11434/v1
    // ...
});
```

That puts the decision with whoever deploys the application rather than whoever
writes the blog posts. It is a guard on what can be configured, not a general SSRF
defence: only address literals and those name suffixes are recognised, so a public
DNS name that resolves into private space passes. Egress firewall rules are the
answer to that, not a validator.

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
`MapBlogIt`.

### Which URLs the blog's redirect table may claim

`UseBlogIt` adds the redirect middleware ahead of your endpoints, and every
authenticated blog user can add redirects. **By default there is no restriction on
the source path**, so a blog author can create a redirect on `/login` or
`/pricing`, shadow that page, and send visitors to an external URL — and a
permanent one stays in browser caches after the row is deleted. BlogIt's own admin,
API and media paths and the root documents it is still serving are always refused,
but nothing else about your application is.

Set `RedirectSourcePrefixes` to confine redirects to the URLs you have handed the
blog:

```csharp
builder.Services.AddBlogIt(options =>
{
    options.RedirectSourcePrefixes = ["/blog", "/archive"];
    // ...
});
```

A source then has to equal a prefix or continue it after a `/`, so `/blog` and
`/blog/2019/old-post` are allowed while `/login` and `/blogger/x` are refused with
a `400`. The check also runs when a redirect is *served*, so setting this stops
honouring rows that already exist — which is the point when a redirect on the
host's login page is already in the table.

One thing to include when you set it: BlogIt writes an *automatic* redirect when a
post's slug changes, on the post's old path — `/{year}/{slug}` by default, wherever
your public routes put it. Prefixes that do not cover those paths mean those
redirects stop being served too, and old links to renamed posts start 404ing.

The default is unrestricted, and deliberately so: a redirect source is a URL the
site no longer serves, which is where the previous site put it rather than
anywhere the blog owns, so a blog-only default would refuse the feature's main
use and would break running deployments on upgrade. If your application has
routes worth protecting from blog authors — and it does, if any authenticated blog
user is not also a site operator — set the prefixes.

### If your application already has authentication

BlogIt registers its own `BlogIt.Jwt` authentication scheme and `BlogIt.Admin`
policy, and that policy names its scheme explicitly, so BlogIt's bearer tokens
are authenticated for BlogIt's endpoints by whichever authorization middleware is
in the pipeline. Your own schemes and policies are untouched: BlogIt sets no
default authenticate, challenge or sign-in scheme.

One consequence to check, and it is ASP.NET Core's rule rather than BlogIt's:
when an application has exactly one authentication scheme and no explicitly
configured default, that single scheme is used as the default. Adding BlogIt adds
a second scheme, so that automatic choice stops applying and `HttpContext.User`
is left unset by `UseAuthentication`. If your host called `AddAuthentication()`
with no scheme name, name your default explicitly —
`AddAuthentication("YourScheme")`, or set `DefaultScheme` in its options — before
adding BlogIt.

Authentication, authorization and rate limiting are pipeline-wide middleware, so
only one copy of each should be in the pipeline. `UseBlogIt` adds all three by
default, which is what a host with no authenticated area of its own wants, and
skips the two auth middlewares when it can see the host already added them:

```csharp
app.UseAuthentication();   // yours
app.UseAuthorization();    // yours
app.UseBlogIt();           // adds neither again
```

Detection works off the marks `UseAuthentication`/`UseAuthorization` leave on the
pipeline, so it only sees calls made **before** `UseBlogIt`. Two cases it cannot
see, both handled by opting out explicitly:

```csharp
app.UseBlogIt(pipeline =>
{
    // The host calls UseRateLimiter itself, anywhere in the pipeline.
    // UseRateLimiter leaves no mark to detect, and two rate limiter middlewares
    // charge two permits for one request — so a 10-attempt login limit starts
    // rejecting at 5.
    pipeline.AddRateLimiterMiddleware = false;
    // The host adds its auth middleware after UseBlogIt.
    pipeline.AddAuthenticationMiddleware = false;
    pipeline.AddAuthorizationMiddleware = false;
});
app.UseAuthentication();
app.UseAuthorization();
```

Opting out of the rate limiter middleware does not opt out of BlogIt's rate
limits: the policies are attached to BlogIt's endpoints and are enforced by
whichever rate limiter middleware runs. If you turn off the auth middlewares,
your own `UseAuthentication`/`UseAuthorization` must still be in the pipeline
before endpoint execution, or ASP.NET Core throws on the first request to a
BlogIt endpoint that carries authorization metadata.

If you also call `AddRateLimiter`, note that `OnRejected` on it is a single
global property. BlogIt does not set it — each BlogIt policy carries its own
`429` handler — so yours stays in force for your policies and BlogIt's rejections
stay `429` regardless of registration order.

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

### Rate limits

`UseBlogIt` installs `UseRateLimiter` unless you opt out (see
[If your application already has authentication](#if-your-application-already-has-authentication))
and BlogIt attaches a fixed-window policy to every anonymous or
credential-touching route. Exceeding one returns `429`.
The limits are not configurable today; they are per-partition, so one caller
tripping a limit does not affect anyone else.

| Routes | Limit | Partitioned by |
| --- | --- | --- |
| `POST /api/auth/login` | 10 / 5 min | Client address |
| `POST /api/auth/change-password`, `POST /api/users` | 20 / 5 min | Bearer token, else client address |
| `GET /api/setup/status`, `POST /api/setup/initialize` | 60 / min | Client address |
| `GET /media/**` | 600 / min | Client address |
| `/rss.xml`, `/atom.xml`, `/sitemap.xml`, `/robots.txt` | 30 / min | Client address |

The media limit is the one worth knowing about: it is sized for real page loads
(one request per image, ~600 permitting ten 60-image gallery views a minute) and
media responses carry `Cache-Control: max-age=31536000`, so returning visitors
re-request nothing. Sites behind a single large shared egress address — a
corporate proxy, carrier-grade NAT — share one partition and so share that
budget. Put a CDN or reverse-proxy cache in front of `/media` if that applies to
you.

The authenticated read and update routes are deliberately not limited: they
already require a valid admin token, and capping them would cap the admin UI's
own paging.

### What `AddBlogIt` always registers

Everything under `AdminPath`, `ApiPath` and `MediaPath`, the four root documents,
and URL redirects are part of the engine and are always registered — there is no
switch to leave the redirect table, the redirect middleware, or the
`/api/redirects` routes out. The root documents are the exception: each of the
four is switched off individually (see
[Feeds, sitemap, and robots.txt](#feeds-sitemap-and-robotstxt)).

AI and analytics are opt-in by installation instead. Their endpoints are always
mapped, but with no satellite package registered they answer `400` with install
instructions (AI) and `404 not configured` (analytics), and no provider services,
credentials, or outbound calls exist. See
[Optional satellite packages](#optional-satellite-packages).

### Migrations

`MigrateBlogItAsync` applies the package's EF Core migrations. It is a deployment
step that the quick-start example happens to run at startup for convenience, and
that convenience has two costs worth deciding about deliberately:

- **Permissions.** Running it at startup means the application's own database
  identity needs schema-modification rights for the whole life of the process,
  not just during deployment. Prefer running migrations as a separate deployment
  step under an identity that has those rights, and running the application under
  one that does not.
- **Concurrent starts.** EF Core migrations are not safe to apply from several
  processes at once. If two instances start together — a rolling deployment, a
  scale-out event, a container restart storm — they can race and one will fail on
  a partially applied migration. One instance, or one deployment step, must own
  it.

Whichever you choose, it must complete before the application begins serving
requests: BlogIt's endpoints assume their tables exist.

## Deployment: BlogIt is single-instance today

BlogIt is designed for one process serving a site. It runs behind a load balancer
only if that balancer sends every request to one instance at a time
(active/passive, or a single instance with restarts). Running two instances of the
same BlogIt site concurrently produces wrong behaviour, not just reduced
performance, and nothing in the engine detects it.

What breaks, and why:

| State | Where it lives | Effect with more than one instance |
| --- | --- | --- |
| Site settings | Whole-table snapshot in a singleton, no expiry, refreshed only by the instance that wrote | A setting changed on instance A is never seen by instance B until B restarts |
| URL redirects | Same | A new or deleted redirect only takes effect on the instance that made the change |
| Preview tokens | Process-local dictionary | A preview link issued by A returns `404` when the balancer sends the click to B |
| Publication scheduling | Hosted service with a timer and no leader election | Every instance processes the same due rows |

The sharpest case is rotating the JWT secret. The signing key is read through the
same settings cache, so after a rotation on instance A, tokens A issues are
rejected by B and tokens B issues are rejected by A — administrators are logged
out at random until every instance has restarted. Rotate the secret with one
instance running, or restart all instances immediately afterwards.

This constraint is about instance count, not about the database. `UseAzureSql`
and its retry-on-failure execution strategy are for surviving transient
connection faults against a managed database, which a single instance needs as
much as several would; they are not an indication that scale-out works.

If you need real scale-out, the missing pieces are a distributed (or
short-TTL) settings and redirect cache, a shared preview-token store, and leader
election for the scheduler. None of them exist today, and BlogIt should not be
deployed as if they did.

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
