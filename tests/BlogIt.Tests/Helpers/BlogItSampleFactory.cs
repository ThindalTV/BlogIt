using BlogIt.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BlogIt.Tests.Helpers;

/// <summary>
/// WebApplicationFactory that uses Testing environment with a unique in-memory DB per instance.
/// </summary>
public class BlogItSampleFactory : WebApplicationFactory<Program>
{
    // Must match what Program.cs seeds in Testing environment
    public static readonly string TestJwtSecret = "test-jwt-secret-that-is-long-enough-for-hmac256";

    private readonly string _dbName = $"BlogItTest_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Pass a unique DB name so each factory instance gets an isolated in-memory database
        builder.UseSetting("TestDbName", _dbName);
    }

}
