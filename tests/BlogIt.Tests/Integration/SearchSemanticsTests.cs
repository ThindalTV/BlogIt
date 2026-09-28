using System.Net.Http.Json;
using BlogIt.Contracts.DTOs;
using BlogIt.Data;
using BlogIt.Entities;
using BlogIt.Services;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Integration;

/// <summary>
/// What a search term means, pinned on every provider the suite can run on. Search used to be
/// <c>string.Contains</c>, which was case-insensitive on SQL Server only because of its default
/// collation, and case-sensitive on SQLite and in memory. See <c>BlogIt.Helpers.SearchPattern</c>.
/// </summary>
public abstract class SearchSemanticsTests<TFactory>(TFactory factory) : IClassFixture<TFactory>
    where TFactory : BlogItSampleFactory
{
    [Fact]
    public async Task PublicSearch_IgnoresCase()
    {
        var marker = NewMarker();
        await SeedPublishedPostAsync($"Learning BLAZOR {marker}");

        var results = await SearchPublicAsync($"blazor {marker}");

        results.Should().ContainSingle().Which.Should().Be($"Learning BLAZOR {marker}");
    }

    [Fact]
    public async Task AdminPostSearch_IgnoresCase()
    {
        var marker = NewMarker();
        var authorId = await SeedPublishedPostAsync($"Mixed Case Title {marker}");
        var client = factory.CreateClient().WithAuth(authorId, $"author_{marker}");

        var response = await client.GetFromJsonAsync<PagedResult<BlogPostSummaryDto>>(
            $"/api/posts?q={Uri.EscapeDataString($"MIXED CASE TITLE {marker}")}");

        response!.Items.Select(post => post.Title).Should().Equal($"Mixed Case Title {marker}");
    }

    [Theory]
    [InlineData("100% off", "1000 off")]
    [InlineData("a_b", "axb")]
    public Task PublicSearch_MatchesWildcardCharactersLiterally(string literal, string lookalike) =>
        AssertMatchesOnlyTheLiteralAsync(literal, lookalike);

    protected async Task AssertMatchesOnlyTheLiteralAsync(string literal, string lookalike)
    {
        var marker = NewMarker();
        await SeedPublishedPostAsync($"{marker} {literal}");
        await SeedPublishedPostAsync($"{marker} {lookalike}");

        var results = await SearchPublicAsync($"{marker} {literal}");

        results.Should().Equal($"{marker} {literal}");
    }

    private static string NewMarker() => Guid.NewGuid().ToString("N");

    private async Task<IReadOnlyList<string>> SearchPublicAsync(string term)
    {
        using var scope = factory.Services.CreateScope();
        var content = scope.ServiceProvider.GetRequiredService<IPublicContentService>();
        var page = await content.SearchPostsAsync(term, page: 1, pageSize: 50);
        return [.. page.Posts.Select(post => post.Title)];
    }

    /// <returns>The id of the post's author, a user created for it.</returns>
    private async Task<Guid> SeedPublishedPostAsync(string title)
    {
        var authorId = await factory.SeedUserAsync($"author_{Guid.NewGuid():N}");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BlogItDbContext>();
        db.BlogPosts.Add(new BlogPost
        {
            Title = title,
            Slug = $"post-{Guid.NewGuid():N}",
            Summary = "Summary",
            Content = "Body",
            IsPublished = true,
            HasBeenPublished = true,
            PublishedAt = DateTime.UtcNow.AddDays(-1),
            AuthorId = authorId
        });
        await db.SaveChangesAsync();
        return authorId;
    }
}

/// <summary>On the suite's default database: in memory, or SQLite when the run asks for it.</summary>
public class DefaultDatabaseSearchSemanticsTests(BlogItSampleFactory factory)
    : SearchSemanticsTests<BlogItSampleFactory>(factory);

/// <summary>Always on SQLite, where <c>Contains</c> became a case-sensitive <c>instr()</c>.</summary>
public class SqliteSearchSemanticsTests(SqliteBlogItSampleFactory factory)
    : SearchSemanticsTests<SqliteBlogItSampleFactory>(factory)
{
    /// <remarks>
    /// Real databases only. EF's in-memory <c>LIKE</c> emulation reads <c>[...]</c> as a SQL Server
    /// character class and mishandles an escaped backslash, so these cannot pass there — but that
    /// provider is a test double, and the pattern is the one EF itself sends SQL Server for
    /// <c>Contains</c>.
    /// </remarks>
    [Theory]
    [InlineData("[draft]", "d")]
    [InlineData(@"C:\temp", "C:temp")]
    public Task PublicSearch_MatchesBracketsAndBackslashesLiterally(string literal, string lookalike) =>
        AssertMatchesOnlyTheLiteralAsync(literal, lookalike);
}
