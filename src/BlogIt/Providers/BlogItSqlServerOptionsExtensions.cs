using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

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
            services.AddSingleton<IBlogItMigrator, EntityFrameworkBlogItMigrator>();
        }
    }
}
