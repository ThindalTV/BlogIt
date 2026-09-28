using Microsoft.Data.Sqlite;

namespace BlogIt;

/// <summary>
/// The configured connection string with its database path resolved against the content root.
/// </summary>
internal sealed class SqliteDatabaseFile
{
    public SqliteDatabaseFile(SqliteConnectionStringBuilder configured, string? contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(configured.ConnectionString);
        var dataSource = builder.DataSource.Trim();

        // A URI filename or a |DataDirectory| placeholder is resolved by SQLite itself, and
        // rewriting either as a path would break it. Everything else is an ordinary file path.
        if (!dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
            && !dataSource.StartsWith('|'))
        {
            FilePath = Path.GetFullPath(
                Path.IsPathRooted(dataSource) || contentRootPath is null
                    ? dataSource
                    : Path.Combine(contentRootPath, dataSource));
            builder.DataSource = FilePath;
        }

        ConnectionString = builder.ConnectionString;
    }

    /// <summary>The connection string EF Core opens.</summary>
    public string ConnectionString { get; }

    /// <summary>
    /// The absolute path of the database file, or <see langword="null"/> when the data source is one
    /// SQLite resolves itself.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Creates the directory the database file goes in. SQLite creates a missing file but not a
    /// missing directory, and fails with the unhelpful "unable to open database file" instead.
    /// </summary>
    public void EnsureDirectoryExists()
    {
        if (Path.GetDirectoryName(FilePath) is { Length: > 0 } directory)
            Directory.CreateDirectory(directory);
    }
}
