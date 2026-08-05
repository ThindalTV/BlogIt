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

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseStaticFiles();
app.UseBlogIt();
app.UseAntiforgery();

app.MapBlogIt();

app.MapRazorComponents<BlogIt.Sample.Components.App>();

app.MapDefaultEndpoints();
app.Run();

public partial class Program;
