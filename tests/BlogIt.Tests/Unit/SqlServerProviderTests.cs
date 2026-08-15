using BlogIt;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Unit;

public class SqlServerProviderTests
{
    private const string ConnectionString =
        "Server=localhost;Database=BlogItRegistrationTests;User Id=test;Password=test;TrustServerCertificate=True";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseSqlServer_RejectsBlankConnectionStrings(string? connectionString)
    {
        var action = () => new BlogItOptions().UseSqlServer(connectionString!);

        action.Should().Throw<ArgumentException>()
            .WithParameterName("connectionString")
            .WithMessage("*must not be empty*");
    }

    [Fact]
    public void UseSqlServer_RegistersFactoryOptionsAndMigrator()
    {
        using var services = CreateServices(sqlOptions => sqlOptions.CommandTimeout(42));

        services.GetRequiredService<IBlogItDatabaseProviderRegistration>()
            .Name.Should().Be("sql-server");
        var migrator = services.GetRequiredService<IBlogItMigrator>();
        migrator.GetType().FullName.Should().Be("BlogIt.EntityFrameworkBlogItMigrator");
        services.GetRequiredService<IBlogItMigrator>().Should().BeSameAs(migrator);

        var factory = services.GetRequiredService<IDbContextFactory<BlogItDbContext>>();
        using var dbContext = factory.CreateDbContext();

        dbContext.Database.ProviderName.Should()
            .Be("Microsoft.EntityFrameworkCore.SqlServer");
        dbContext.Database.GetCommandTimeout().Should().Be(42);
    }

    [Fact]
    public void UseSqlServer_DiscoversModelAndPackagedMigrations()
    {
        using var services = CreateServices();
        var factory = services.GetRequiredService<IDbContextFactory<BlogItDbContext>>();
        using var dbContext = factory.CreateDbContext();

        typeof(BlogItDbContext).Assembly.GetName().Name.Should().Be("BlogIt");
        dbContext.Model.FindEntityType(typeof(BlogPost)).Should().NotBeNull();
        dbContext.Model.FindEntityType(typeof(Tag)).Should().NotBeNull();
        dbContext.Database.GetMigrations().Should().Equal(
            "20260805232419_InitialCreate",
            "20260814204048_AddSetupLock",
            "20260814205906_AddAiConversationSummary",
            "20260814220335_AddAiMessageIsCompacted",
            "20260815221704_AddAppUserSecurityStamp");
    }

    private static ServiceProvider CreateServices(
        Action<Microsoft.EntityFrameworkCore.Infrastructure.SqlServerDbContextOptionsBuilder>?
            sqlServerOptionsAction = null)
    {
        var services = new ServiceCollection();
        services.AddBlogIt(options =>
        {
            options.UseSqlServer(ConnectionString, sqlServerOptionsAction);
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
}
