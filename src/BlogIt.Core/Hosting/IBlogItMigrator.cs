namespace BlogIt;

public interface IBlogItMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
