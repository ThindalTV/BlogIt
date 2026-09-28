using BlogIt.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BlogIt;

internal sealed class SqliteDatabaseProviderRegistration(
    SqliteConnectionStringBuilder connectionString,
    Action<SqliteDbContextOptionsBuilder>? sqliteOptionsAction)
    : IBlogItDatabaseProviderRegistration
{
    public string Name => "sqlite";

    public void RegisterServices(IServiceCollection services)
    {
        // Resolved from the container rather than here because the content root is only known once
        // the host is built; registration runs inside AddBlogIt, before that.
        services.AddSingleton(provider => new SqliteDatabaseFile(
            connectionString,
            provider.GetService<IHostEnvironment>()?.ContentRootPath));

        services.AddDbContextFactory<BlogItDbContext>((provider, options) =>
            ConfigureDbContext(
                options,
                provider.GetRequiredService<SqliteDatabaseFile>().ConnectionString,
                sqliteOptionsAction));

        services.AddSingleton<IBlogItMigrator>(provider => new SqliteBlogItMigrator(
            provider.GetRequiredService<SqliteDatabaseFile>(),
            EntityFrameworkBlogItMigrator.Create(provider)));
    }

    /// <summary>
    /// The one place the EF Core options for SQLite are built, shared with the design-time factory
    /// so the migrations are generated against exactly the model the application runs.
    /// </summary>
    internal static void ConfigureDbContext(
        DbContextOptionsBuilder options,
        string connectionString,
        Action<SqliteDbContextOptionsBuilder>? sqliteOptionsAction = null) =>
        options
            .UseSqlite(connectionString, sqliteOptions =>
            {
                sqliteOptionsAction?.Invoke(sqliteOptions);
                // This assembly, not BlogIt.Core: Core carries the SQL Server migrations, and EF
                // migrations are provider-specific SQL.
                sqliteOptions.MigrationsAssembly(
                    typeof(SqliteDatabaseProviderRegistration).Assembly.GetName().Name);
            })
            .ReplaceService<IModelCustomizer, SqliteBlogItModelCustomizer>();
}
