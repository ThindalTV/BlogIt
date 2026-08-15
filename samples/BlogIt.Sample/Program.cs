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
});

builder.Services.AddRazorComponents();

// TEMPORARY manual-testing scaffolding — see ManualTestAnalyticsService.cs. Remove this line and
// that file once manual browser testing of the dashboard's Analytics panel is complete.
builder.Services.AddScoped<BlogIt.Services.IAnalyticsService, BlogIt.Sample.ManualTestAnalyticsService>();

var app = builder.Build();
await app.MigrateBlogItAsync();

// Renders a real 404 with real HTML for public post/page URLs that don't resolve to published
// content — see NotFoundResponseMiddleware for why this can't just be MapRazorComponents +
// HttpContext.Response.StatusCode / NavigationManager.NotFound(): both were tried and both hit
// hard framework issues (silently discarded body; a second NavigationManager initializing in
// the same request scope) that a raw HtmlRenderer pass sidesteps entirely.
app.UseMiddleware<BlogIt.Sample.NotFoundResponseMiddleware>();

app.UseStaticFiles();
app.UseBlogIt();
app.UseAntiforgery();

app.MapBlogIt();

app.MapRazorComponents<BlogIt.Sample.Components.App>();

app.MapDefaultEndpoints();
app.Run();

public partial class Program;
