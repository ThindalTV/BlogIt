using BlogIt.Data;
using BlogIt.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlogIt;

internal sealed class EntityFrameworkBlogItMigrator(
    IDbContextFactory<BlogItDbContext> dbContextFactory,
    ILogger<EntityFrameworkBlogItMigrator> logger) : IBlogItMigrator
{
    /// <summary>
    /// How many posts to load and recount per round trip during the word-count backfill.
    /// </summary>
    /// <remarks>
    /// Small because each row carries a full post body: the point is to bound peak memory on a large
    /// blog, not to minimise round trips on a one-off pass.
    /// </remarks>
    private const int BackfillBatchSize = 100;

    /// <summary>
    /// Builds the migrator from a container that has a <see cref="BlogItDbContext"/> factory
    /// registered. Shared by every EF-backed database provider, including the satellite ones.
    /// </summary>
    /// <remarks>
    /// Resolves the logger by hand rather than by constructor injection so that logging stays
    /// optional. A host that only migrates — a console entry point, a provisioning step — may never
    /// have called AddLogging, and requiring it here would turn a missing convenience into a
    /// startup crash.
    /// </remarks>
    internal static EntityFrameworkBlogItMigrator Create(IServiceProvider services) =>
        new(
            services.GetRequiredService<IDbContextFactory<BlogItDbContext>>(),
            services.GetService<ILogger<EntityFrameworkBlogItMigrator>>()
                ?? NullLogger<EntityFrameworkBlogItMigrator>.Instance);

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await BackfillWordCountsAsync(dbContext, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fills in <see cref="Entities.BlogPost.WordCount"/> for posts written before the column
    /// existed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Needed because the count is computed on save: without a backfill, every post that predates
    /// the column reports no word count until someone happens to edit it, and a host showing reading
    /// times would render nothing on most of its archive. A SQL default cannot help — the value
    /// comes from rendering markdown.
    /// </para>
    /// <para>
    /// Best-effort by design. A failure here is logged and swallowed rather than propagated: the
    /// migration itself has already succeeded at this point, and a cosmetic column must never be
    /// the reason an application refuses to start. The next startup tries again.
    /// </para>
    /// </remarks>
    private async Task BackfillWordCountsAsync(
        BlogItDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Summary-only posts are included rather than skipped, so they settle on 0 —
                // "counted, and there is nothing to read" — instead of staying null forever and
                // being indistinguishable from a row this pass never reached.
                var batch = await dbContext.BlogPosts
                    .Where(post => post.WordCount == null)
                    .Take(BackfillBatchSize)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (batch.Count == 0)
                    break;

                foreach (var post in batch)
                    post.WordCount = MarkdownHelper.CountWords(post.Content);

                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Backfilled word counts for {Count} BlogIt posts.", batch.Count);

                if (batch.Count < BackfillBatchSize)
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Could not backfill BlogIt post word counts. Listings will omit reading times for "
                + "posts written before the WordCount column was added; startup continues and the "
                + "backfill is retried next time.");
        }
    }
}
