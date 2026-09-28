# BlogIt.Sqlite

`BlogIt.Sqlite` stores a BlogIt site in a single SQLite database file instead of
SQL Server. It suits small and self-hosted blogs: nothing to install or run
beside the application, and a backup is a file copy. It depends transitively on
the same package version of `BlogIt`; do not install a separate `BlogIt` version
alongside it.

## Requirements and install

Use .NET 10. No database server is needed.

```powershell
dotnet add package BlogIt.Sqlite
```

## Startup

```csharp
using BlogIt;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBlogIt(options =>
{
    options.UseSqlite("Data Source=App_Data/blogit.db");
    options.UseFileSystemStorage(storage =>
        storage.RootPath = Path.Combine("App_Data", "blogit-media"));
});

var app = builder.Build();
await app.MigrateBlogItAsync();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseBlogIt();

app.MapBlogIt();
app.Run();
```

`MigrateBlogItAsync` creates the database file, and the directory it goes in,
on first start, then applies this package's migrations on every start after.

## Behaviour to know about

- **Relative paths resolve against the content root**, the same rule filesystem
  media storage follows, not against the process working directory that SQLite
  itself would use. A Windows service or IIS worker usually starts somewhere
  else entirely.
- **Text comparisons ignore case**, as they do under SQL Server's default
  collation. Logging in as `Admin` or `admin` finds the same account, and two
  usernames, slugs, or redirect sources that differ only by case are
  duplicates. SQLite's `NOCASE` folds ASCII letters only.
- **Search ignores case for ASCII letters.** Non-ASCII text matches only in the
  same case.
- **In-memory databases are refused.** An in-memory SQLite database disappears
  when its last connection closes, and EF Core closes connections after each
  operation, so every post would be lost silently.

## Deployment

BlogIt is single-instance whichever database it uses; see "Deployment: BlogIt is
single-instance today" in the technical guide. With SQLite that also means:

- Keep the database file on a **local disk**. SQLite's file locking is unreliable
  on network file systems, including SMB shares and the `/home` share Azure App
  Service mounts, and a database kept there can be corrupted.
- SQLite allows one writer at a time. A blog's writes come from a handful of
  authors, so this rarely matters. BlogIt keeps SQLite's default rollback
  journal. On a local disk, you can switch to write-ahead logging so readers never wait
  for a writer. The setting is stored in the file, so run it once:

  ```csharp
  await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
      "Data Source=App_Data/blogit.db");
  await connection.OpenAsync();
  await using var command = connection.CreateCommand();
  command.CommandText = "PRAGMA journal_mode=WAL;";
  await command.ExecuteNonQueryAsync();
  ```

- **Back up** with the application stopped, or with SQLite's online backup
  (`VACUUM INTO 'backup.db'`). If write-ahead logging is on, a plain copy of a
  running database also needs its `-wal` file.

## Moving between SQL Server and SQLite

The two providers use the same model but separate migrations, and BlogIt has no
built-in export. To move a site, copy the data table by table with your own
tooling, starting from a database that `MigrateBlogItAsync` has created for the
target provider.
