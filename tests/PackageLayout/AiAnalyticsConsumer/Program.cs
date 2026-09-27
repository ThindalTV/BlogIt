using BlogIt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBlogIt(options =>
{
    options.UseSqlServer(
        "Server=localhost;Database=PackageProof;User Id=package-proof;Password=package-proof;TrustServerCertificate=True");
    options.UseFileSystemStorage();
    // Both extensions have to be reachable from the packages alone - no ProjectReference, no
    // BlogIt PackageReference - and neither takes configuration, because both providers read their
    // credentials and model names from the saved site settings.
    options.UseOpenAi();
    options.UseGoogleAnalytics();
});

var app = builder.Build();
app.UseBlogIt();
app.MapBlogIt();
app.Run();
