using BlogIt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBlogIt(options =>
{
    // Relative on purpose: verify.ps1 asserts the file lands under the content root, in a directory
    // that did not exist before startup.
    options.UseSqlite("Data Source=App_Data/blogit.db");
    options.UseFileSystemStorage(storage => storage.RootPath = Path.Combine("App_Data", "media"));
});

var app = builder.Build();
await app.MigrateBlogItAsync();
app.UseBlogIt();
app.MapBlogIt();
app.Run();
