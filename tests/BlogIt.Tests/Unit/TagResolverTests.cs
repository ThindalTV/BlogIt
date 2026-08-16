using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

public class TagResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReusesExistingTagsAndDeduplicatesNames()
    {
        await using var db = CreateContext();
        var existing = new Tag { Name = ".NET", Slug = "net" };
        db.Tags.Add(existing);
        await db.SaveChangesAsync();

        var tags = await TagResolver.ResolveAsync(db, [".NET", "net", "Blazor"]);

        tags.Should().HaveCount(2);
        tags.Should().ContainSingle(tag => tag.Id == existing.Id);
        tags.Should().ContainSingle(tag => tag.Slug == "blazor");
    }

    [Fact]
    public async Task ResolveAsync_DropsANameTooLongForTheColumn()
    {
        // The AI export path feeds this whatever the model produced for "Tags:", so the guard cannot
        // live only in the API validation that answers a person with a 400. Dropping matches how
        // blank and unslugifiable names are already handled.
        await using var db = CreateContext();

        var tags = await TagResolver.ResolveAsync(
            db, ["keep", new string('x', ContentLimits.TagNameLength + 1)]);

        tags.Should().ContainSingle().Which.Name.Should().Be("keep");
    }

    [Fact]
    public async Task ResolveAsync_KeepsANameExactlyAtTheLimit()
    {
        await using var db = CreateContext();
        var atTheLimit = new string('y', ContentLimits.TagNameLength);

        var tags = await TagResolver.ResolveAsync(db, [atTheLimit]);

        tags.Should().ContainSingle().Which.Name.Should().Be(atTheLimit);
    }

    [Fact]
    public void Validate_ReportsAnOverLongName_AndAcceptsTheLimit()
    {
        var errors = new Dictionary<string, string[]>();
        TagResolver.Validate(errors, ["fine", new string('x', ContentLimits.TagNameLength + 1)]);
        var atTheLimit = new Dictionary<string, string[]>();
        TagResolver.Validate(atTheLimit, [new string('x', ContentLimits.TagNameLength)]);

        errors.Should().ContainKey("tagNames");
        atTheLimit.Should().BeEmpty();
    }

    private static BlogItDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BlogItDbContext(options);
    }
}
