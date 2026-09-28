using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BlogIt;

public static class BlogItSqliteOptionsExtensions
{
    /// <summary>
    /// Configures BlogIt to store everything in a single SQLite database file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A relative <c>Data Source</c> resolves against the host's content root, the same rule
    /// filesystem media storage follows, rather than the process working directory SQLite itself
    /// would use — a service or IIS worker usually starts somewhere else entirely. The directory is
    /// created by <c>MigrateBlogItAsync</c> if it does not exist yet.
    /// </para>
    /// <para>
    /// Text comparisons are case-insensitive, matching SQL Server's default collation: a username,
    /// slug or redirect source that differs from an existing one only by case is the same value,
    /// for lookups and for the unique indexes alike.
    /// </para>
    /// <para>
    /// SQLite allows one writer at a time. That suits a blog — writes come from a handful of
    /// authors, reads from everyone — but BlogIt is single-instance regardless, and one database
    /// file must never be shared by several application instances or placed on a network share.
    /// </para>
    /// </remarks>
    /// <param name="options">The options being configured.</param>
    /// <param name="connectionString">
    /// A SQLite connection string naming a database file, for example
    /// <c>Data Source=App_Data/blogit.db</c>.
    /// </param>
    /// <param name="sqliteOptionsAction">Optional further configuration of the EF Core provider.</param>
    /// <returns>The same options instance, for chaining.</returns>
    /// <exception cref="ArgumentException">
    /// The connection string is empty, cannot be parsed, names no file, or names an in-memory
    /// database.
    /// </exception>
    public static BlogItOptions UseSqlite(
        this BlogItOptions options,
        string connectionString,
        Action<SqliteDbContextOptionsBuilder>? sqliteOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseDatabaseProvider(
            new SqliteDatabaseProviderRegistration(
                ValidateConnectionString(connectionString),
                sqliteOptionsAction));
    }

    private static SqliteConnectionStringBuilder ValidateConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "The BlogIt SQLite connection string must not be empty.",
                nameof(connectionString));
        }

        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            throw new ArgumentException(
                "The BlogIt SQLite connection string is invalid.",
                nameof(connectionString),
                exception);
        }

        // An empty Data Source is a private temporary database SQLite deletes when the connection
        // closes, which EF does after every operation.
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new ArgumentException(
                "The BlogIt SQLite connection string must name a database file, for example "
                + "'Data Source=App_Data/blogit.db'.",
                nameof(connectionString));
        }

        // Rejected rather than tolerated: an in-memory database lives only while a connection to it
        // is open, and EF opens and closes one per operation, so the schema MigrateBlogItAsync
        // creates would be gone before the first request — every post silently lost, not an error.
        if (builder.Mode == SqliteOpenMode.Memory
            || string.Equals(builder.DataSource.Trim(), ":memory:", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "BlogIt cannot use an in-memory SQLite database: its contents are discarded whenever "
                + "the last connection closes. Name a database file instead.",
                nameof(connectionString));
        }

        return builder;
    }
}
