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
    public async Task ServingMedia_CountsViews_ButNotRangeContinuationsOrHeadRequests()
    {
        var username = $"viewer_{Guid.NewGuid():N}";
        var userId = await factory.SeedUserAsync(username);
        var client = factory.CreateClient().WithAuth(userId, username);

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent("view counted bytes"u8.ToArray());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "views.png");
        var uploaded = await (await client.PostAsync("/api/media/upload", form))
            .Content.ReadFromJsonAsync<MediaFileDto>();
        uploaded!.ViewCount.Should().Be(0);

        (await client.GetAsync(uploaded.PublicPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(uploaded.PublicPath)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var head = new HttpRequestMessage(HttpMethod.Head, uploaded.PublicPath);
        await client.SendAsync(head);
        using var continuation = new HttpRequestMessage(HttpMethod.Get, uploaded.PublicPath);
        continuation.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(5, null);
        await client.SendAsync(continuation);

        var page = await client.GetFromJsonAsync<PagedResult<MediaFileDto>>(
            "/api/media?q=views");
        page!.Items.Single(m => m.Id == uploaded.Id).ViewCount.Should().Be(2);
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
