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
    var isAspireRun =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__BlogItDb"))
        || !string.IsNullOrWhiteSpace(builder.Configuration["services:blogit-sample-sql:tcp:0"])
        || !string.IsNullOrWhiteSpace(builder.Configuration["services:blogit-sample:http:0"]);

    // See SampleDatabaseConnection for why this is a separate class and what each flag decides.
    databaseConnection = BlogIt.Sample.SampleDatabaseConnection.Require(
        builder.Configuration.GetConnectionString("BlogItDb"),
        isAspireRun,
        builder.Environment.IsDevelopment());
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

// No IAnalyticsService registration. A stub returning hardcoded session and user counts used to be
// substituted here for manual testing of the dashboard's Analytics panel; it was deleted rather than
// gated further, because the engine's own not-configured path now logs and reports its state
// distinguishably, and a sample is copied wholesale — invented numbers that look like real traffic are
// the last thing to hand an integrator. Analytics remains a host-substitutable abstraction:
// implementing BlogIt.Services.IAnalyticsService and registering it here overrides the default without
// installing BlogIt.GoogleAnalytics or referencing any Google SDK.

var app = builder.Build();

if (isTesting)
{
    // Loud on purpose. The Testing environment swaps the database and media storage for in-memory
    // ones, which used to happen in complete silence: a deploy that inherited ASPNETCORE_ENVIRONMENT
    // =Testing looked healthy, accepted posts and uploads, and lost every one of them on restart.
    app.Logger.LogWarning(
        "BlogIt.Sample is running in the Testing environment: the database and media storage are "
        + "IN-MEMORY and ALL CONTENT IS LOST ON SHUTDOWN. This environment exists for the automated "
        + "test suite only — if this is a deployment, set ASPNETCORE_ENVIRONMENT and configure "
        + "ConnectionStrings:BlogItDb.");
}
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
