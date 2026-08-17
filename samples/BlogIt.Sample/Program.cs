using BlogIt;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args
});
var hostTestDbName = builder.Configuration["TestDbName"];

// Explicit precedence: base settings -> environment settings -> injected environment -> secrets.
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        optional: true,
        reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>(optional: true, reloadOnChange: true);

builder.AddServiceDefaults();

var isTesting = builder.Environment.IsEnvironment("Testing");
string? databaseConnection = null;
if (!isTesting)
{
    databaseConnection = builder.Configuration.GetConnectionString("BlogItDb");
    if (string.IsNullOrWhiteSpace(databaseConnection))
    {
        throw new InvalidOperationException(
            "Missing connection string 'BlogItDb'. Start via BlogIt.Sample.AppHost so Aspire injects the SQL connection.");
    }

    var isAspireRun =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__BlogItDb"))
        || !string.IsNullOrWhiteSpace(builder.Configuration["services:blogit-sample-sql:tcp:0"])
        || !string.IsNullOrWhiteSpace(builder.Configuration["services:blogit-sample:http:0"]);
    if (isAspireRun
        && (databaseConnection.Contains("Trusted_Connection=True", StringComparison.OrdinalIgnoreCase)
            || databaseConnection.Contains("Integrated Security=True", StringComparison.OrdinalIgnoreCase)
            || databaseConnection.Contains("mssqllocaldb", StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException(
            $"Invalid BlogItDb connection for this setup: '{databaseConnection}'. Use Aspire-provisioned SQL Server credentials.");
    }
}

var configuredMediaRoot = builder.Configuration["BlogIt:Storage:RootPath"];
if (string.IsNullOrWhiteSpace(configuredMediaRoot))
    configuredMediaRoot = Path.Combine("App_Data", "blogit-media");

var mediaRoot = Path.GetFullPath(
    Path.IsPathRooted(configuredMediaRoot)
        ? configuredMediaRoot
        : Path.Combine(builder.Environment.ContentRootPath, configuredMediaRoot));

builder.Services.AddBlogIt(options =>
{
    if (isTesting)
    {
        options.UseDatabaseProvider(
            new TestingDatabaseProviderRegistration(
                hostTestDbName ?? $"BlogItTest_{Guid.NewGuid():N}"));
        options.UseStorageProvider(new TestingStorageProviderRegistration());
        return;
    }

    options.UseSqlServer(databaseConnection!);
    options.UseFileSystemStorage(storage => storage.RootPath = mediaRoot);
    // From the BlogIt.OpenAi satellite package. Without it the admin's AI screens answer 400 with
    // installation instructions; the provider itself reads its key and model names from the saved
    // site settings, so there is nothing to pass here.
    options.UseOpenAi();
});

builder.Services.AddRazorComponents();

if (!isTesting)
{
    // TEMPORARY manual-testing scaffolding — see ManualTestAnalyticsService.cs. Remove this block
    // and that file once manual browser testing of the dashboard's Analytics panel is complete.
    // Gated on !isTesting so the integration suite sees the engine's real analytics default — the
    // not-configured 404 a host gets without BlogIt.GoogleAnalytics — rather than this stub.
    builder.Services.AddScoped<BlogIt.Services.IAnalyticsService, BlogIt.Sample.ManualTestAnalyticsService>();
}

var app = builder.Build();
await app.MigrateBlogItAsync();

// Renders a real 404 with real HTML for public post/page URLs that don't resolve to published
// content — see NotFoundResponseMiddleware for why this can't just be MapRazorComponents +
// HttpContext.Response.StatusCode / NavigationManager.NotFound(): both were tried and both hit
// hard framework issues (silently discarded body; a second NavigationManager initializing in
// the same request scope) that a raw HtmlRenderer pass sidesteps entirely.
//
// UseRouting is called explicitly, and first, so the middleware can see which endpoint the request
// matched: it buffers the response only for Razor component page renders, because buffering
// everything meant every media download was read into memory in full before any of it was sent.
// Without an explicit UseRouting here, WebApplication inserts routing for us but the endpoint is not
// resolved yet at this position in the pipeline, and the middleware would see null for every request.
app.UseRouting();
app.UseMiddleware<BlogIt.Sample.NotFoundResponseMiddleware>();

app.UseStaticFiles();
app.UseBlogIt();
app.UseAntiforgery();

app.MapBlogIt();

app.MapRazorComponents<BlogIt.Sample.Components.App>();

app.MapDefaultEndpoints();
app.Run();

public partial class Program;
