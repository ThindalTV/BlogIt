using BlogIt.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlogIt;

/// <summary>
/// Design-time factory used by EF Core CLI migrations for the SQLite provider.
/// </summary>
/// <remarks>
/// Every model change needs a migration here as well as in BlogIt.Core; the migration-coverage
/// tests fail until both exist. From the repository root:
/// <c>dotnet ef migrations add MigrationName --project src/providers/BlogIt.Sqlite</c>
/// </remarks>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
internal sealed class BlogItSqliteDbContextFactory : IDesignTimeDbContextFactory<BlogItDbContext>
{
    public BlogItDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BlogItDbContext>();
        SqliteDatabaseProviderRegistration.ConfigureDbContext(
            optionsBuilder,
            "Data Source=blogit-design-time.db");
        return new BlogItDbContext(optionsBuilder.Options);
    }
}
