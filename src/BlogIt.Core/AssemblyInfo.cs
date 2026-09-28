using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("BlogIt.Tests")]
// The SQLite satellite reuses EntityFrameworkBlogItMigrator rather than copying its word-count
// backfill. Safe across packages because a satellite pins the engine to its exact version.
[assembly: InternalsVisibleTo("BlogIt.Sqlite")]
