using BlogIt.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlogIt.Web;

/// <summary>
/// Design-time factory used by EF Core CLI migrations.
/// Run from the BlogIt.Web directory: dotnet ef migrations add InitialCreate --project ../BlogIt.Shared
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class BlogItDbContextFactory : IDesignTimeDbContextFactory<BlogItDbContext>
{
    public BlogItDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BlogItDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=BlogIt;Trusted_Connection=True;MultipleActiveResultSets=true");
        return new BlogItDbContext(optionsBuilder.Options);
    }
}
