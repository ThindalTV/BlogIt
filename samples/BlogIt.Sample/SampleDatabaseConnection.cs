namespace BlogIt.Sample;

/// <summary>
/// The sample's guard on the <c>BlogItDb</c> connection string.
/// </summary>
/// <remarks>
/// A separate class rather than inline in <c>Program.cs</c> so the combinations can be tested: the
/// checks have to run before the host is built, and startup clears and rebuilds the configuration
/// sources, so nothing a test can set short of a process environment variable reaches them — and
/// setting one is itself an Aspire signal, which would make the interesting cases untestable.
/// </remarks>
public static class SampleDatabaseConnection
{
    /// <summary>
    /// Returns the connection string, or throws if the sample is not configured to reach a database
    /// it should be using.
    /// </summary>
    /// <param name="connectionString">Whatever configuration resolved for <c>BlogItDb</c>.</param>
    /// <param name="isAspireRun">
    /// Whether Aspire's injected connection string or service endpoints are present. An Aspire run
    /// provisions its own SQL container, so reaching machine-local credentials instead means the
    /// injection did not happen and the run is silently using a different database.
    /// </param>
    /// <param name="isDevelopment">
    /// Whether the host environment is Development. Windows-integrated, machine-local credentials are
    /// a legitimate way for a developer to run the sample against their own SQL instance; they are
    /// never right for a deployed environment, where they mean nobody configured the database.
    /// </param>
    public static string Require(string? connectionString, bool isAspireRun, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing connection string 'BlogItDb'. Start via BlogIt.Sample.AppHost so Aspire "
                + "injects the SQL connection, or set ConnectionStrings:BlogItDb (user secrets or the "
                + "ConnectionStrings__BlogItDb environment variable) to your own SQL Server. The "
                + "sample deliberately ships no default: a committed one would be silently used by "
                + "any deployment that forgot to configure this.");
        }

        // The condition used to be isAspireRun alone, which meant a plain `dotnet run` in Production
        // accepted machine-local credentials without a word — and the sample shipped exactly such a
        // string in appsettings.json, so that was the default path, not an edge case.
        var isMachineLocal =
            connectionString.Contains("Trusted_Connection=True", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Integrated Security=True", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("mssqllocaldb", StringComparison.OrdinalIgnoreCase);

        if (isMachineLocal && (isAspireRun || !isDevelopment))
        {
            throw new InvalidOperationException(
                $"Invalid BlogItDb connection for this setup: '{connectionString}'. "
                + (isAspireRun
                    ? "An Aspire run must use the Aspire-provisioned SQL Server credentials; reaching "
                      + "machine-local SQL means the injected connection string never arrived."
                    : "Machine-local SQL credentials (LocalDB, Trusted_Connection, Integrated "
                      + "Security) are only accepted in Development. Configure a real database for "
                      + "this environment."));
        }

        return connectionString;
    }
}
