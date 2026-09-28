using System.Net;
using System.Net.Http.Json;
using BlogIt.Contracts.DTOs;
using BlogIt.Data;
using BlogIt.Entities;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

/// <summary>
/// The SQLite provider against a real database file, through the full host. The rest of the suite
/// can be run on SQLite too (<c>BLOGIT_TEST_DATABASE=sqlite</c>); these are the behaviours that are
/// about SQLite specifically, so they run on it every time.
/// </summary>
public class SqliteProviderIntegrationTests(SqliteBlogItSampleFactory factory)
    : IClassFixture<SqliteBlogItSampleFactory>
{
    [Fact]
    public async Task Startup_MigratesARealSqliteFile()
    {
        // Building the client starts the host, which runs MigrateBlogItAsync.
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();

        db.Database.ProviderName.Should().Be("Microsoft.EntityFrameworkCore.Sqlite");
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        File.Exists(factory.SqlitePath).Should().BeTrue();
    }

    [Fact]
    public async Task Login_IgnoresUsernameCase_AsOnSqlServer()
    {
        var username = $"CaseUser_{Guid.NewGuid():N}";
        await factory.SeedUserAsync(username, "Password1!");

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(username.ToLowerInvariant(), "Password1!"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateUser_WithAUsernameDifferingOnlyByCase_Conflicts()
    {
        var existing = $"taken_{Guid.NewGuid():N}";
        var adminId = await factory.SeedUserAsync(existing);
        var client = factory.CreateClient().WithAuth(adminId, existing);

        var response = await client.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(existing.ToUpperInvariant(), "Shouting Duplicate", "Password1!"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UniqueIndexes_TreatValuesDifferingOnlyByCaseAsDuplicates()
    {
        // Straight through the context, past the API's own existence check, so this is the index
        // itself refusing — the backstop the API relies on when two requests race.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        var slug = $"tag-{Guid.NewGuid():N}";
        db.Tags.Add(new Tag { Name = "First", Slug = slug });
        await db.SaveChangesAsync();

        db.Tags.Add(new Tag { Name = "Second", Slug = slug.ToUpperInvariant() });
        var save = () => db.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateException>();
    }
}
