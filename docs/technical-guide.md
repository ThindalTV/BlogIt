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
provider, migrate after building the app, and add BlogIt's middleware and
endpoints:

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

The three paths must be distinct. Place forwarding, exception handling, HTTPS,
and static files before `UseBlogIt`. Place host antiforgery after it, then call
`MapBlogIt`. BlogIt registers and invokes its own `BlogIt.Jwt` authentication
scheme and `BlogIt.Admin` policy; the host must not add duplicate authentication
or authorization middleware specifically for BlogIt.

`MigrateBlogItAsync` applies the package's EF Core migrations. Run it before the
application begins serving requests and use a database identity with schema
change permission during deployment.

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

BlogIt maps `/rss.xml`, `/atom.xml`, `/sitemap.xml`, and `/robots.txt`
automatically. Set the site URL and description in the admin portal so generated
absolute URLs and metadata are correct.

See `samples/BlogIt.Sample` for archive, post, page, search, tag, preview, SEO,
and analytics examples.

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
