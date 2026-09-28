using BlogIt.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Every shipped database provider carries its own migrations, generated from the same model. A model
/// change that gets a SQL Server migration but not a SQLite one would leave SQLite hosts failing at
/// startup — EF refuses to migrate a database whose model has moved on — and nothing else in the suite
/// would notice, because it runs on the in-memory provider by default.
/// </summary>
/// <remarks>
/// Neither check opens a connection: they compare each provider's model snapshot with the current model.
/// </remarks>
public class MigrationCoverageTests
{
    [Fact]
    public void SqlServerMigrations_AreUpToDateWithTheModel()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=BlogItMigrationCoverage;User Id=test;Password=test",
                sql => sql.MigrationsAssembly(typeof(BlogItDbContext).Assembly.GetName().Name))
            .Options;
        using var dbContext = new BlogItDbContext(options);

        dbContext.Database.HasPendingModelChanges().Should().BeFalse(
            "run: dotnet ef migrations add <Name> --project src/BlogIt.Core");
    }

    [Fact]
    public void SqliteMigrations_AreUpToDateWithTheModel()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>();
        SqliteDatabaseProviderRegistration.ConfigureDbContext(options, "Data Source=coverage.db");
        using var dbContext = new BlogItDbContext(options.Options);

        dbContext.Database.HasPendingModelChanges().Should().BeFalse(
            "run: dotnet ef migrations add <Name> --project src/providers/BlogIt.Sqlite");
    }
}
