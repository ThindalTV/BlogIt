using BlogIt.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlogIt;

/// <summary>
/// Design-time factory used by EF Core CLI migrations.
/// Run from the repository root: dotnet ef migrations add MigrationName --project src/BlogIt
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class BlogItDbContextFactory : IDesignTimeDbContextFactory<BlogItDbContext>
{
    public BlogItDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BlogItDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=BlogIt;Trusted_Connection=True;MultipleActiveResultSets=true",
            sqlOptions => sqlOptions.MigrationsAssembly(typeof(BlogItDbContext).Assembly.GetName().Name));
        return new BlogItDbContext(optionsBuilder.Options);
    }
}
