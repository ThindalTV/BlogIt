using Microsoft.EntityFrameworkCore;

namespace BlogIt.Shared.Data;

public static class BlogItDbContextTransactionExtensions
{
    /// <summary>
    /// Runs <paramref name="operation"/> inside an explicit transaction opened through the
    /// context's configured execution strategy, so the whole transaction is retried as a unit
    /// when connection retries are enabled (see
    /// <see cref="BlogIt.BlogItAzureSqlOptionsExtensions.UseAzureSql"/>). A bare
    /// <c>Database.BeginTransactionAsync()</c> is not safe to call directly once retries are
    /// enabled — it throws, because a retrying strategy needs to own the transaction to retry it
    /// safely. Only needed for multi-step writes that must be atomic together; a single
    /// <c>SaveChangesAsync()</c> call doesn't need this, EF Core already retries it as-is.
    /// </summary>
    public static async Task ExecuteInTransactionAsync(
        this BlogItDbContext db,
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await operation();
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
