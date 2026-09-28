namespace BlogIt;

/// <summary>
/// Creates the database file's directory, then applies the SQLite migrations through the engine's
/// own migrator so the post-migration backfills run exactly as they do on SQL Server.
/// </summary>
/// <remarks>
/// The journal mode is deliberately left alone. Write-ahead logging would let readers and the writer
/// proceed together, but it is persisted in the file and corrupts databases kept on network file
/// systems — including the <c>/home</c> share Azure App Service mounts — so switching it on is the
/// host's decision, not a side effect of starting up. The README shows how.
/// </remarks>
internal sealed class SqliteBlogItMigrator(
    SqliteDatabaseFile databaseFile,
    IBlogItMigrator inner) : IBlogItMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        databaseFile.EnsureDirectoryExists();
        return inner.MigrateAsync(cancellationToken);
    }
}
