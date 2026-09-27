using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlogIt;

public static class BlogItSqlServerOptionsExtensions
{
    public static BlogItOptions UseSqlServer(
        this BlogItOptions options,
        string connectionString,
        Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "The BlogIt SQL Server connection string must not be empty.",
                nameof(connectionString));
        }

        return options.UseDatabaseProvider(
            new SqlServerDatabaseProviderRegistration(connectionString, sqlServerOptionsAction));
    }

    private sealed class SqlServerDatabaseProviderRegistration(
        string connectionString,
        Action<SqlServerDbContextOptionsBuilder>? sqlServerOptionsAction)
        : IBlogItDatabaseProviderRegistration
    {
        public string Name => "sql-server";

        public void RegisterServices(IServiceCollection services)
        {
            services.AddDbContextFactory<BlogIt.Shared.Data.BlogItDbContext>(options =>
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlServerOptionsAction?.Invoke(sqlOptions);
                    sqlOptions.MigrationsAssembly(
                        typeof(BlogIt.Shared.Data.BlogItDbContext).Assembly.GetName().Name);
                }));
            // Resolved by hand rather than by constructor injection so that logging stays optional.
            // A host that only migrates — a console entry point, a provisioning step — may never
            // have called AddLogging, and requiring it here would turn a missing convenience into a
            // startup crash.
            services.AddSingleton<IBlogItMigrator>(provider => new EntityFrameworkBlogItMigrator(
                provider.GetRequiredService<
                    IDbContextFactory<BlogIt.Shared.Data.BlogItDbContext>>(),
                provider.GetService<ILogger<EntityFrameworkBlogItMigrator>>()
                    ?? NullLogger<EntityFrameworkBlogItMigrator>.Instance));
        }
    }
}
