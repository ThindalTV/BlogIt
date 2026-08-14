using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BlogIt;

public static class BlogItAzureSqlOptionsExtensions
{
    /// <summary>
    /// Configures BlogIt to use Azure SQL Database. This is the same SQL Server provider as
    /// <see cref="BlogItSqlServerOptionsExtensions.UseSqlServer"/>, but with EF Core's
    /// connection-retry strategy (<c>EnableRetryOnFailure</c>) turned on by default, since Azure
    /// SQL Database is far more prone to transient faults — throttling, failover, elastic pool
    /// moves — than a dedicated SQL Server instance.
    /// </summary>
    /// <remarks>
    /// Once a retrying execution strategy is configured, EF Core requires any EXPLICIT
    /// transaction to be opened through that strategy so the whole transaction can be retried
    /// as a unit — a bare <c>Database.BeginTransactionAsync()</c> throws
    /// <c>InvalidOperationException</c> ("The configured execution strategy does not support
    /// user-initiated transactions"). Use
    /// <see cref="BlogIt.Shared.Data.BlogItDbContextTransactionExtensions.ExecuteInTransactionAsync"/>
    /// for any multi-step write that needs to be atomic. Single <c>SaveChangesAsync()</c> calls
    /// need no special handling — EF Core already retries those automatically.
    /// </remarks>
    public static BlogItOptions UseAzureSql(
        this BlogItOptions options,
        string connectionString,
        Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null) =>
        options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
            sqlServerOptionsAction?.Invoke(sqlOptions);
        });
}
