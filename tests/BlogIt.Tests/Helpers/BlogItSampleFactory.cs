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

    /// <summary>
    /// The security stamp <see cref="TestHelpers.SeedUserAsync"/> writes and
    /// <see cref="TestHelpers.CreateToken"/> signs into tokens by default, so the two agree
    /// without every test having to thread the value through. Authentication compares the token's
    /// stamp against the stored row on each request, so a test that wants a revoked token just
    /// passes a different one.
    /// </summary>
    public const string DefaultTestSecurityStamp = "test-security-stamp";

    private readonly string _dbName = $"BlogItTest_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Pass a unique DB name so each factory instance gets an isolated in-memory database
        builder.UseSetting("TestDbName", _dbName);
    }

}
