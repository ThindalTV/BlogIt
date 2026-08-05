using BlogIt.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace BlogIt;

internal sealed class EntityFrameworkBlogItMigrator(
    IDbContextFactory<BlogItDbContext> dbContextFactory) : IBlogItMigrator
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }
}
