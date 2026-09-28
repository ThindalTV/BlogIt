using BlogIt.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BlogIt.Tests.Helpers;

/// <summary>
/// WebApplicationFactory that uses Testing environment with a unique database per instance —
/// in-memory by default, or a SQLite file when <c>BLOGIT_TEST_DATABASE=sqlite</c>.
/// </summary>
/// <remarks>
/// The in-memory provider is fast but forgiving: it enforces no foreign keys, raises the wrong
/// exception for a duplicate key, and compares text ordinally. Setting the environment variable runs
/// every test that uses this factory against a real relational database with the shipped
/// migrations instead, which is how CI proves the SQLite provider and not just the engine.
/// </remarks>
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

    /// <summary>Whether this run was asked to use SQLite for every factory.</summary>
    public static bool SqliteRequested { get; } = string.Equals(
        Environment.GetEnvironmentVariable("BLOGIT_TEST_DATABASE"),
        "sqlite",
        StringComparison.OrdinalIgnoreCase);

    private readonly string _dbName = $"BlogItTest_{Guid.NewGuid():N}";

    public BlogItSampleFactory()
        : this(SqliteRequested)
    {
    }

    protected BlogItSampleFactory(bool useSqlite)
    {
        if (useSqlite)
            SqlitePath = Path.Combine(Path.GetTempPath(), "blogit-tests", $"{_dbName}.db");
    }

    /// <summary>The SQLite database file, or <see langword="null"/> when running in memory.</summary>
    public string? SqlitePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // Pass a unique DB name so each factory instance gets an isolated in-memory database
        builder.UseSetting("TestDbName", _dbName);
        if (SqlitePath is not null)
            builder.UseSetting("TestSqlitePath", SqlitePath);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        DeleteSqliteFiles();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            DeleteSqliteFiles();
    }

    /// <remarks>
    /// Best-effort: the connection string disables pooling so no handle should outlive the host, but
    /// a leftover file in the temp directory is not worth failing a test over.
    /// </remarks>
    private void DeleteSqliteFiles()
    {
        if (SqlitePath is null)
            return;

        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            try
            {
                File.Delete(SqlitePath + suffix);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>
/// Always runs on a SQLite file, whatever <c>BLOGIT_TEST_DATABASE</c> says — for tests that are
/// about SQLite behaviour specifically.
/// </summary>
public class SqliteBlogItSampleFactory() : BlogItSampleFactory(useSqlite: true);
