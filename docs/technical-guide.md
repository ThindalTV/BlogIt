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

Writing a *client* rather than hosting the blog — a console tool, a MAUI app,
another service — needs neither. Install `BlogIt.Contracts` instead: the request
and response records on their own, with no dependencies at all. See
[Writing a client](#writing-a-client-blogitcontracts).

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
| `BlogIt.GoogleAnalytics` | `GET /api/analytics/summary` returns `404 "Analytics is not configured."` — the same answer as an installed provider with no credentials entered. The client-side tag is unaffected: `GaScript` loads the Google Tag Manager container, lives in `BlogIt`, and needs no SDK. |

### Analytics reporting requires a tag container

The container ID and the reporting credentials are stored settings, not host
configuration, but they are not independent of each other. The container ID is
the Google Tag Manager container `GaScript` loads into the page — it is what
collects the traffic, and it is where visitor consent is obtained — while the
GA4 property ID and service-account JSON only read that traffic back.
`AnalyticsPolicy`, in `BlogIt.Contracts`, is the single definition of the
resulting rules: a container ID must be shaped like one (`GTM-` followed by
letters and digits, so a GA4 `G-…` measurement ID is refused rather than
interpolated into a container URL that will not serve it), and reporting may only
be configured alongside one.

Both `POST /api/setup/initialize` and `PUT /api/settings` enforce it and answer
`400` otherwise, the settings route judging the *effective* settings rather than
the request body so a partial update cannot slip a property ID past a stored
container ID that is blank. The bundled admin runs the same code to hide the
fields it would be refused for; a client of your own should do the same, but the
server is the authority.

Both satellites are also replaceable rather than merely omittable: `IAiService` and
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

`MaxMediaUploadBytes` sets the largest accepted media upload, defaulting to 50 MB.
It is applied to BlogIt's own upload endpoint, so it governs regardless of the
server's global request-body limit — a host whose server defaults to 30 MB does
not silently cap the blog — and an oversize upload gets a `413` whose body names
the limit rather than an empty response the portal cannot explain. It does not
reach limits enforced outside the application: reverse proxies and IIS apply their
own caps and reject before the request arrives, so raising this much above the
default means raising those to match.

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
`MapBlogIt`. Forgetting either call, and some of the ways of getting the order
wrong, are reported at startup — see [Startup checks](#startup-checks).

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

Every redirect is one an administrator entered in the admin portal, or one you
created through `IUrlRedirectService`. BlogIt writes none of its own: a post slug
is locked at first publication, so a published post never changes URL and there is
no rename for the engine to follow.

The default is unrestricted, and deliberately so: a redirect source is a URL the
site no longer serves, which is where the previous site put it rather than
anywhere the blog owns, so a blog-only default would refuse the feature's main
use and would break running deployments on upgrade. If your application has
routes worth protecting from blog authors — and it does, if any authenticated blog
user is not also a site operator — set the prefixes.

BlogIt logs the effective policy once at startup, at information level, so which
of the two you are running is visible in the boot log rather than only in the
configuration. It is not a warning: the unrestricted default is a deliberate
choice, and a warning about intended behaviour is one people learn to ignore.

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

### Claiming a site without the wizard

A fresh database has no user, and no user means nothing can authenticate — the
admin API cannot create the first account because creating an account requires an
administrator. Normally a person completes the `/blogit` wizard. For CI,
end-to-end tests, containers and provisioned environments, `InitializeBlogItAsync`
does the same thing from code:

```csharp
await app.MigrateBlogItAsync();
await app.InitializeBlogItAsync(new BlogItSetupRequest
{
    Username = "owner",
    DisplayName = "Site Owner",
    Password = adminPassword,
    SiteName = "Example",
    SiteUrl = "https://example.com"
});
await app.RunAsync();
```

AI and analytics settings are optional here even though the wizard asks for them.
It returns `true` if this call claimed the site and `false` if it was already
claimed, so a container entry point can call it on every start; an invalid request
throws, because that means the calling code is wrong rather than that the site is
in a particular state. It runs the same validation and the same setup lock as the
HTTP route, so two replicas racing to claim one database resolve safely and the
loser simply gets `false`.

There is no option to disable the setup endpoint, and none is needed: it refuses
once a user exists, so calling this before `Run()` closes it before the first
request is served.

### Startup checks

`AddBlogIt` registers a startup filter that verifies the wiring once the pipeline
is built, and fails with the fix named rather than letting the mistake surface
later:

- **`UseBlogIt` or `MapBlogIt` never called** — throws. Both are easy to miss and
  neither failure is obvious: without `MapBlogIt` nothing is reachable, and
  without `UseBlogIt` the application still starts and the admin portal still
  works, but no URL redirect ever fires.
- **A route mapped by both BlogIt and the host** — throws, naming the route and
  the option that gives it back. Previously this was an `AmbiguousMatchException`
  on the first request to the path.
- **A file in `wwwroot` shadowed by a BlogIt root document** — warns. Static files
  are registered before `UseBlogIt`, but routing runs first and the static-file
  middleware stands aside once an endpoint matches, so `wwwroot/sitemap.xml` is
  silently never served while `ServeSitemap` is on.
- **`UseAntiforgery` before `UseBlogIt`** — warns. Nothing in BlogIt needs
  antiforgery today, so this is a deviation from the documented order rather than
  a fault.

Two ordering rules remain undetectable and are still only conventions: calling
`UseStaticFiles` after `UseBlogIt`, and registering your own `UseRateLimiter`.
Neither leaves a mark anything can inspect afterwards.

## Deployment: BlogIt is single-instance today

BlogIt is designed for one process serving a site. It runs behind a load balancer
only if that balancer sends every request to one instance at a time
(active/passive, or a single instance with restarts). Running two instances of the
same BlogIt site concurrently produces wrong behaviour, not just reduced
performance, and nothing in the engine detects it.

What breaks, and why:

| State | Where it lives | Effect with more than one instance |
| --- | --- | --- |
| Site settings | Whole-table snapshot in a singleton, 30-second expiry | A setting changed on instance A reaches instance B within 30 seconds |
| URL redirects | Same | A new or deleted redirect takes effect on other instances within 30 seconds |
| Preview tokens | Process-local dictionary | A preview link issued by A returns `404` when the balancer sends the click to B |
| Publication scheduling | Hosted service with a timer and no leader election | Every instance processes the same due rows |

The two caches expire rather than living forever, which bounds most of the damage
to seconds. That includes the case that used to be sharpest: the JWT signing key
is read through the settings cache, so a rotation now propagates within the same
30 seconds instead of leaving administrators logged out at random until every
instance restarted. It is still worth rotating with one instance running.

The remaining two rows have no such bound. A preview link is only valid on the
instance that issued it, and session affinity is the only workaround. The
scheduler has no leader election, so every instance processes the same due rows —
harmless in the sense that they reach the same end state, but it is duplicated
work and duplicated writes.

**Nothing detects a second instance, and nothing is going to.** Rolling restarts,
slot swaps and container rolling updates all run two instances briefly on every
deployment, so from the database there is no way to tell a misconfiguration from a
normal deploy in progress. A check would either warn on every release or miss the
case it exists for. Instance count is a deployment decision; keep it to one.

This constraint is about instance count, not about the database. `UseAzureSql`
and its retry-on-failure execution strategy are for surviving transient
connection faults against a managed database, which a single instance needs as
much as several would; they are not an indication that scale-out works.

If you need real scale-out, the missing pieces are now a shared preview-token
store and leader election for the scheduler. Neither exists today, and BlogIt
should not be deployed as if they did.

## Writing a client: `BlogIt.Contracts`

The admin API's request and response records are not part of the engine
assembly. They live in `BlogIt.Contracts`, which packs and versions on its own
and has **zero dependencies** — no EF Core, no SQL Server client, no BCrypt, no
ASP.NET Core framework reference. That is what makes it takeable from a console
tool, a MAUI app, a WebAssembly client, or another service:

```powershell
dotnet add package BlogIt.Contracts
```

A host does **not** install it. `BlogIt` takes an exact-version dependency on
the matching `BlogIt.Contracts`, so the records arrive transitively; referencing
both only creates a version to keep in step by hand.

The package is browser-safe, and the bundled Blazor WebAssembly admin compiles
against exactly these types — which is the standing proof that nothing
server-only has leaked in.

### What is in it

| Namespace | Contents |
| --- | --- |
| `BlogIt.Shared.DTOs` | The request and response records: posts, pages, tags, media, redirects, users, settings, setup, auth, previews, AI and analytics. |
| `BlogIt.Shared` | `SettingKeys` for the well-known per-site settings, and the `ContentLimits`, `SeoLimits` and `RedirectLimits` ceilings. |
| `BlogIt.Shared.Helpers` | `BlogUrlHelper` (build the same public post path the server routes), `OptionalText` (the null-vs-empty convention), and `PasswordPolicy`. |

The namespaces are `BlogIt.Shared.*` while the assembly and package are
`BlogIt.Contracts`. The mismatch is deliberate and documented in
[docs/publishing.md](publishing.md); it is a candidate for the 1.0 cut, not
before.

### The limit constants are the schema's widths

`ContentLimits`, `SeoLimits` and `RedirectLimits` are not client-side advice.
Each constant is load-bearing in three places at once — the EF column width, the
server-side check that returns a `400` instead of letting the value fail on
`SaveChanges`, and the data annotation on the DTO. They are shared rather than
copied precisely so those three cannot drift apart.
`RedirectLimits.SourcePathLength` is the sharpest example: 450 characters is 900
bytes of `nvarchar`, which is what keeps the unique index on that column inside
SQL Server's 1700-byte nonclustered key limit.

Nothing here bounds the unbounded columns. A post's summary and content and a
page's content stay `nvarchar(max)`; the only thing the database refuses there
is null.

### Validate before you send — but the server is still the authority

The records carry `System.ComponentModel.DataAnnotations` attributes for the
limits whose constants live in this package, so a client can reject a bad
payload without a round trip:

```csharp
using System.ComponentModel.DataAnnotations;
using BlogIt.Shared.DTOs;

var request = new CreateBlogPostRequest(
    Title: title,
    Summary: summary,
    Content: markdown,
    SeoTitle: null,
    SeoDescription: null,
    SeoKeywords: null,
    OgImageUrl: null,
    TagNames: []);

List<ValidationResult> failures = [];
if (!Validator.TryValidateObject(
        request, new ValidationContext(request), failures, validateAllProperties: true))
{
    // Fix these first; the server rejects the same values with a 400.
}
```

Those attributes are a **subset**, and passing them is not a promise the server
will accept the payload. They cover the ceilings this package already declares
as constants — title, slug, tag, SEO and redirect lengths — and nothing else.
Rules whose authority is a server-side validator (slug character rules, URL
scheme checks, settings coherence) are deliberately **not** restated here:
copying those numbers across an assembly boundary would create a second source
of truth that drifts silently. Treat a `400` with a problem-details body as the
last word.

`PasswordPolicy` is the exception that proves the shape of the rule. It is not a
restatement — it is the single definition, moved into this assembly so the
client and the server run *the same code* rather than two copies of the same
rules. The client calls `PasswordPolicy.Validate` so the user hears about a weak
password immediately; `AuthService` calls it again on arrival, because a
client-side check is advice, not enforcement.

### Optional text: null and empty are different

BlogIt distinguishes them. `null` means the value does not exist; `""` means it
exists and is empty. A client normalises on the way in with
`OptionalText.OrNull`, so a field the author never filled in is stored as
`null`, and reads back out with `OptionalText.FirstPresent` rather than `??`.
Null-coalescing only skips `null`, so `post.SeoTitle ?? post.Title` returns `""`
for any row that stored a blank SEO title. Rows like that exist, so readers have
to tolerate them whatever the writers do from now on.

### Appending a parameter is binary-breaking

These records grow by appending constructor parameters with defaults —
`ScheduledPublishAt`, `ScheduleState`, `HasBeenPublished` and `ConcurrencyStamp`
all arrived that way. That is source-compatible and **binary-breaking**: a
client compiled against the old record calls a constructor arity that no longer
exists, and the failure is a runtime `MissingMethodException`, not a build
error. Recompiling is the whole remedy.

Two things follow for a client author. Construct these records with **named
arguments**, so an appended parameter cannot silently rebind a positional call.
And read the release notes before letting a client lag the server's contracts
version. The full compatibility policy — what a minor bump means, what a major
one means, and when to prefer an init-only property over a positional parameter
— is in [docs/publishing.md](publishing.md).

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

`BlogPostDetailDto` and `PageDto` — both in
[`BlogIt.Contracts`](#writing-a-client-blogitcontracts) — carry a
`ConcurrencyStamp`. `PUT /posts/{id}`
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
| `GetPostsByDateRangeAsync(from, to, page, pageSize)` | Paginated posts published in `[from, to)` |
| `GetArchiveCountsAsync()` | Published post counts per UTC month, newest first |
| `GetPostAsync(slug, includeNavigation)` | One published post and optional adjacent posts |
| `GetPageAsync(slug)` | One published custom page |
| `GetTagAsync(slug)` | One tag, or `null` when no such tag exists |

Every method is published-only. "Published" means `IsPublished` is set *and*
`PublishedAt` has a value, so a post scheduled for a future date is excluded
too. Drafts return `null` rather than the content.

If you query the entities yourself, use the `WherePublished()` extension in
`BlogIt.Shared.Data` rather than writing that rule out again:

```csharp
var posts = await db.BlogPosts.WherePublished()
    .OrderByDescending(post => post.PublishedAt)
    .ToListAsync();
```

There is a `Page` overload too, and it is deliberately a different rule — a page
has no publication instant, so the flag is the whole condition. Filtering posts
on `IsPublished` alone publishes posts that no BlogIt listing returns.

Every paged result carries `TotalCount` alongside `Page` and `TotalPages`, so
"Found 47 posts" needs no second query.

### Month archives and reading time

`GetPostsByDateRangeAsync` takes instants rather than a year and a month, because
BlogIt has no notion of a site timezone and a `(year, month)` signature would
silently pick one. For UTC months:

```csharp
var from = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
var page = await Content.GetPostsByDateRangeAsync(from, from.AddMonths(1), 1, 10);
```

A site that wants local months converts its own boundaries and passes those.
`GetArchiveCountsAsync` buckets by UTC month, so a post published within an
offset's distance of midnight on the first or last of a month can be counted in
the neighbouring bucket. Cache its result — it only changes when something is
published.

`BlogPostSummaryDto.WordCount` carries the word count of the post's body, so a
listing can show a reading time without loading every body. It is `null` only for
a row written before the column existed and not yet backfilled, and `0` for a
summary-only post. How many words per minute to assume, and whether to show a
reading time at all, is yours.

### Timestamps are UTC instants

Every timestamp BlogIt returns is a `DateTimeOffset` at offset zero. Format with
`"o"` where an offset is required — Open Graph's `article:published_time`, Atom —
or call `ToLocalTime()` to display it. These were `DateTime` before 0.2.0 and came
back with `DateTimeKind.Unspecified`, which formatted with no offset at all; if
you have host code compensating for that, it can be deleted.

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

Summaries are Markdown too, and they may contain links. That matters if your card
component is itself a link: an `<a>` cannot contain another `<a>`, and browsers
repair the invalid nesting by closing the outer anchor early — which visually
breaks the card rather than producing an error anyone would notice in review.
Either link only the title or a call-to-action and leave the card body
non-interactive, or strip inline links when rendering a summary inside a linked
card.

### Canonical URLs come from the configured site URL

Build canonical URLs from the site URL saved in admin settings, not from
`NavigationManager.BaseUri`. `BaseUri` reflects whichever hostname answered the
request, so a site reachable on an apex domain, a `www` subdomain and a staging
host would declare a different canonical on each — which defeats the point of
declaring one at all. The configured site URL is also what `BlogFeed.SiteUrl` and
`SitemapEntry.Location` are built from, so taking canonicals from the same place
keeps the three consistent by construction.

### What BlogIt deliberately does not decide

Some things a blog needs are presentation policy, and BlogIt supplies the inputs
without an opinion on the output:

- **Reading time.** `WordCount` is on the summary DTO; words per minute, rounding,
  and whether to show it are yours.
- **Related posts.** Tags are on every summary DTO, and `GetPostsByTagAsync` is
  the query. What counts as "related" is a product decision.
- **Pagination chrome.** `Page`, `TotalPages` and `TotalCount` are on every paged
  result. Whether that renders as numbered links, prev/next, or infinite scroll is
  a design decision.
- **Whether a page shows its title.** `Page.Title` is metadata — it feeds the SEO
  title, the admin listing, slug generation and the sitemap — and rendering it is
  your template's call. If some pages should not show a heading, put the `#`
  heading in the page's own Markdown and stop rendering `Title` in the template,
  or skip the template heading when the content already opens with one.

The package also provides `BlogIt.Components.Shared.SeoHead` and `GaScript`.
Compose them into host pages for metadata, structured data, canonical URLs, and
Google Tag Manager. `GaScript` emits markup only after a container ID is saved
in admin settings. It renders Google's standard GTM snippet, with the loader
written out as a plain async `<script>` element pointing at `gtm.js` rather than
injected from JavaScript — same DOM, same load. The `<noscript>` iframe half of
Google's snippet is not emitted, because BlogIt contributes to the document head
only.

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
mapped is not neutral: a host static file at the same path is silently shadowed by
BlogIt's endpoint, and a host *endpoint* at the same path is a duplicate route.
Both are now reported at startup — the shadowed file as a warning, the duplicate
route as a failure naming the switch — rather than being discovered on a request
that never reaches the right handler. See "Startup checks". Turning the switch off
makes the path the host's again.

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
dotnet test .\BlogIt.slnx -c Release --no-build
```

The tests live in three projects — `BlogIt.Tests.Shared`, `BlogIt.Tests.Web` and
`BlogIt.Tests.MAUI` — so run the solution rather than naming one. On a machine
without the MAUI workloads installed, use `.\BlogIt.Web.slnx` instead: it is the
same set minus the MAUI projects, and it works for both `build` and `test`.

Package verification lives under `build/package-layout-tests`; it validates package
contents and a clean consumer application, and the release workflow runs it against the
packages it just packed, before publishing.
