using BlogIt;
using BlogIt.Data;
using BlogIt.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace BlogIt.Tests.Unit;

public class SqliteProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseSqlite_RejectsBlankConnectionStrings(string? connectionString)
    {
        var action = () => new BlogItOptions().UseSqlite(connectionString!);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("connectionString")
            .WithMessage("*must not be empty*");
    }

    [Fact]
    public void UseSqlite_RejectsUnparseableConnectionStrings()
    {
        var action = () => new BlogItOptions().UseSqlite("Server=localhost;Database=BlogIt");

        action.Should().Throw<ArgumentException>()
            .WithParameterName("connectionString")
            .WithMessage("*invalid*");
    }

    [Fact]
    public void UseSqlite_RejectsConnectionStringsThatNameNoFile()
    {
        // A blank Data Source is a temporary database SQLite deletes as soon as EF closes it.
        var action = () => new BlogItOptions().UseSqlite("Mode=ReadWriteCreate");

        action.Should().Throw<ArgumentException>()
            .WithParameterName("connectionString")
            .WithMessage("*must name a database file*");
    }

    [Theory]
    [InlineData("Data Source=:memory:")]
    [InlineData("Data Source=blogit;Mode=Memory;Cache=Shared")]
    public void UseSqlite_RejectsInMemoryDatabases(string connectionString)
    {
        var action = () => new BlogItOptions().UseSqlite(connectionString);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("connectionString")
            .WithMessage("*in-memory*");
    }

    [Fact]
    public void UseSqlite_CannotBeCombinedWithAnotherDatabaseProvider()
    {
        var action = () => new BlogItOptions()
            .UseSqlServer("Server=localhost;Database=BlogIt;User Id=test;Password=test")
            .UseSqlite("Data Source=blogit.db");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*'sql-server' is already configured; 'sqlite' cannot also be configured*");
    }

    [Fact]
    public void UseSqlite_RegistersFactoryOptionsAndMigrator()
    {
        using var services = CreateServices(
            "Data Source=blogit.db",
            contentRoot: null,
            sqliteOptions => sqliteOptions.CommandTimeout(42));

        services.GetRequiredService<IBlogItDatabaseProviderRegistration>()
            .Name.Should().Be("sqlite");
        services.GetRequiredService<IBlogItMigrator>()
            .Should().BeOfType<SqliteBlogItMigrator>();

        using var dbContext = services
            .GetRequiredService<IDbContextFactory<BlogItDbContext>>()
            .CreateDbContext();

        dbContext.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
        dbContext.Database.GetCommandTimeout().Should().Be(42);
        dbContext.Database.GetMigrations().Should().ContainSingle()
            .Which.Should().EndWith("_InitialCreate",
                "the SQLite migrations ship in BlogIt.Sqlite, not the SQL Server ones in BlogIt.Core");
    }

    [Fact]
    public void UseSqlite_ResolvesRelativePathsAgainstTheContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "blogit-content-root");
        using var services = CreateServices("Data Source=App_Data/blogit.db", contentRoot);

        var expected = Path.GetFullPath(Path.Combine(contentRoot, "App_Data", "blogit.db"));
        var file = services.GetRequiredService<SqliteDatabaseFile>();
        file.FilePath.Should().Be(expected);

        using var dbContext = services
            .GetRequiredService<IDbContextFactory<BlogItDbContext>>()
            .CreateDbContext();
        dbContext.Database.GetConnectionString().Should().Contain(expected);
    }

    [Fact]
    public void UseSqlite_LeavesAbsolutePathsAlone()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "elsewhere", "blog.db"));
        using var services = CreateServices(
            $"Data Source={absolute}",
            Path.Combine(Path.GetTempPath(), "blogit-content-root"));

        services.GetRequiredService<SqliteDatabaseFile>().FilePath.Should().Be(absolute);
    }

    [Fact]
    public void UseSqlite_LeavesPlaceholdersForSqliteToResolve()
    {
        using var services = CreateServices(
            "Data Source=|DataDirectory|blogit.db",
            Path.Combine(Path.GetTempPath(), "blogit-content-root"));

        var file = services.GetRequiredService<SqliteDatabaseFile>();
        file.FilePath.Should().BeNull();
        file.ConnectionString.Should().Contain("|DataDirectory|blogit.db");
    }

    [Fact]
    public async Task Migrate_CreatesAMissingDirectoryAndTheSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), "blogit-tests", $"migrate-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "nested", "blogit.db");
        try
        {
            using (var services = CreateServices($"Data Source={path};Pooling=False", contentRoot: null))
            {
                await services.GetRequiredService<IBlogItMigrator>().MigrateAsync();

                await using var dbContext = await services
                    .GetRequiredService<IDbContextFactory<BlogItDbContext>>()
                    .CreateDbContextAsync();
                (await dbContext.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
                (await dbContext.BlogPosts.CountAsync()).Should().Be(0);
            }

            File.Exists(path).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Model_GivesEveryTextColumnACaseInsensitiveCollation()
    {
        using var services = CreateServices("Data Source=blogit.db", contentRoot: null);
        using var dbContext = services
            .GetRequiredService<IDbContextFactory<BlogItDbContext>>()
            .CreateDbContext();

        var textColumns = dbContext.GetService<IDesignTimeModel>()
            .Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Where(property => property.ClrType == typeof(string))
            .ToList();

        textColumns.Should().Contain(property =>
            property.DeclaringType.ClrType == typeof(AppUser) && property.Name == nameof(AppUser.Username));
        textColumns.Should().OnlyContain(property => property.GetCollation() == "NOCASE",
            "SQLite compares bytes by default, and the engine relies on SQL Server's case-insensitive "
            + "comparison for logins, slug lookups and its unique indexes");
    }

    private static ServiceProvider CreateServices(
        string connectionString,
        string? contentRoot,
        Action<SqliteDbContextOptionsBuilder>? sqliteOptions = null)
    {
        var services = new ServiceCollection();
        if (contentRoot is not null)
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services.AddBlogIt(options =>
        {
            options.UseSqlite(connectionString, sqliteOptions);
            options.UseStorageProvider(new TestStorageProvider());
        });
        return services.BuildServiceProvider();
    }

    private sealed class TestStorageProvider : IBlogItStorageProviderRegistration
    {
        public string Name => "test-storage";

        public void RegisterServices(IServiceCollection services)
        {
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "BlogIt.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
