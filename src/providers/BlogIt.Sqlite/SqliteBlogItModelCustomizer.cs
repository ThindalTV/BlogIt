using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace BlogIt;

/// <summary>
/// Gives every text column SQLite's <c>NOCASE</c> collation, so the engine compares text the way it
/// does on SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// The engine was written against SQL Server's default case-insensitive collation and leans on it
/// without saying so: logging in checks <c>Username == request.Username</c>, the slug and redirect
/// lookups use <c>==</c>, and the unique indexes on those columns are what stop two authors taking
/// "Admin" and "admin". SQLite compares bytes by default, so without this a login would become
/// case-sensitive and the unique indexes would admit near-duplicates the SQL Server build refuses.
/// A column collation fixes all of them at once, because SQLite applies it to <c>=</c>, to
/// <c>ORDER BY</c> and to the column's indexes.
/// </para>
/// <para>
/// <c>NOCASE</c> folds ASCII only. That is narrower than SQL Server's collation but covers what
/// these columns hold in practice — slugs are ASCII by construction.
/// </para>
/// <para>
/// Applied here rather than in <see cref="Data.BlogItDbContext"/> because the collation name is
/// SQLite's: a SQL Server database would reject it. Search does not depend on this — see
/// <c>BlogIt.Helpers.SearchPattern</c> for why it uses <c>LIKE</c> instead.
/// </para>
/// </remarks>
internal sealed class SqliteBlogItModelCustomizer(ModelCustomizerDependencies dependencies)
    : RelationalModelCustomizer(dependencies)
{
    internal const string Collation = "NOCASE";

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetDeclaredProperties())
                     .Where(property => property.ClrType == typeof(string)))
        {
            property.SetCollation(Collation);
        }
    }
}
