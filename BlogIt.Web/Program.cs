using BlogIt.Shared.Data;
using BlogIt.Web.Services;
using BlogIt.Web.Api;
using BlogIt.Web.Middleware;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args
});
var hostTestDbName = builder.Configuration["TestDbName"];

// Make configuration source precedence explicit:
// appsettings.json -> appsettings.{Environment}.json -> Aspire/injected environment variables -> user secrets.
builder.Configuration.Sources.Clear();
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>(optional: true, reloadOnChange: true);

builder.AddServiceDefaults();

// EF Core — skip SQL Server registration in Testing (replaced by in-memory DB in test factory)
if (!builder.Environment.IsEnvironment("Testing"))
{
    var connectionString = builder.Configuration.GetConnectionString("BlogItDb");
    if (string.IsNullOrWhiteSpace(connectionString))
        throw new InvalidOperationException(
            "Missing connection string 'BlogItDb'. Start via BlogIt.AppHost so Aspire injects the SQL connection.");

    // Guard against accidental integrated-auth/localdb overrides only when running under Aspire.
    var isAspireRun = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__BlogItDb")) ||
                      !string.IsNullOrWhiteSpace(builder.Configuration["services:blogit-sql:tcp:0"]) ||
                      !string.IsNullOrWhiteSpace(builder.Configuration["services:blogit-web:http:0"]);
    if (isAspireRun &&
        (connectionString.Contains("Trusted_Connection=True", StringComparison.OrdinalIgnoreCase) ||
         connectionString.Contains("Integrated Security=True", StringComparison.OrdinalIgnoreCase) ||
         connectionString.Contains("mssqllocaldb", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException(
            $"Invalid BlogItDb connection for this setup: '{connectionString}'. Use Aspire-provisioned SQL Server credentials.");

    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("BlogItStorage")))
        throw new InvalidOperationException(
            "Missing connection string 'BlogItStorage'. Start via BlogIt.AppHost so Aspire injects the blog storage connection.");

    builder.Services.AddDbContextFactory<BlogItDbContext>(options =>
        options.UseSqlServer(connectionString));
}else
    builder.Services.AddDbContextFactory<BlogItDbContext>(options =>
        options.UseInMemoryDatabase(
            hostTestDbName ?? "BlogItTestDb"));

// Services
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBlobService, BlobService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IAiService, AiService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPreviewTokenService, PreviewTokenService>();
builder.Services.AddSingleton<IUrlRedirectService, UrlRedirectService>();
builder.Services.AddScoped<IPublicContentService, PublicContentService>();
builder.Services.AddHostedService<PublicationSchedulingService>();

// JWT auth — signing key loaded from DB at request time
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = async context =>
            {
                var settingsService = context.HttpContext.RequestServices.GetRequiredService<ISettingsService>();
                var secret = await settingsService.GetAsync(BlogIt.Shared.SettingKeys.JwtSecret);
                if (!string.IsNullOrEmpty(secret))
                {
                    context.Options.TokenValidationParameters.IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
                }
            }
        };
    });

builder.Services.AddAuthorization();

// Blazor (public site SSR + WASM host)
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Apply relational migrations before any service can query the database.
// Tests use EnsureCreated because the in-memory provider does not support migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
    else
    {
        db.Database.EnsureCreated();
    }
}

app.Use(async (context, next) =>
{
    if (context.Request.Path == "/blogit")
    {
        context.Response.Redirect("/blogit/");
        return;
    }

    await next();
});

app.UseStaticFiles();
app.UseUrlRedirects();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Minimal API route groups
app.MapSetupApi();
app.MapPreviewApi();
app.MapAuthApi();
app.MapPostsApi();
app.MapPagesApi();
app.MapMediaApi();
app.MapUsersApi();
app.MapSettingsApi();
app.MapAiApi();
app.MapAnalyticsApi();
app.MapMediaProxyApi();
app.MapSitemapApi();
app.MapFeedsApi();
app.MapRedirectsApi();

// WASM admin shell served at /blogit
app.UseBlazorFrameworkFiles("/blogit");
app.MapFallbackToFile("/blogit/{**path:nonfile}", "blogit/index.html")
    .Add(endpoint => ((RouteEndpointBuilder)endpoint).Order = -1);

// Public Blazor SSR
app.MapRazorComponents<BlogIt.Web.Components.App>()
    .AddInteractiveWebAssemblyRenderMode();

app.MapDefaultEndpoints();
app.Run();

// Accessible to integration tests
public partial class Program { }
