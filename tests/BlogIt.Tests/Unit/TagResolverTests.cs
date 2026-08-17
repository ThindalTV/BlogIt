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

    [Theory]
    [InlineData("Программирование")]
    [InlineData("日本語")]
    [InlineData("!!!")]
    public async Task ResolveAsync_KeepsATagWhoseNameSlugifiesToNothing(string name)
    {
        // Finding #47: these used to be dropped silently — the post saved, the tag never attached,
        // and nothing reported it.
        await using var db = CreateContext();

        var tags = await TagResolver.ResolveAsync(db, [name]);

        var tag = tags.Should().ContainSingle().Subject;
        // The name the author typed is what the tag list shows, so only the URL carries the token.
        tag.Name.Should().Be(name);
        tag.Slug.Should().Be(SlugHelper.SlugifyOrFallback(name));
        tag.Slug.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_GivesTwoDifferentNonLatinNamesDifferentTags()
    {
        await using var db = CreateContext();

        var tags = await TagResolver.ResolveAsync(db, ["Программирование", "日本語"]);

        tags.Should().HaveCount(2);
        tags.Select(tag => tag.Slug).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task ResolveAsync_ReusesTheExistingTagForANonLatinName()
    {
        // The fallback slug is content-derived and stable, so the second post finds the first
        // post's tag rather than colliding on the unique slug index.
        await using var db = CreateContext();
        var existing = new Tag { Name = "日本語", Slug = SlugHelper.SlugifyOrFallback("日本語") };
        db.Tags.Add(existing);
        await db.SaveChangesAsync();

        var tags = await TagResolver.ResolveAsync(db, ["日本語"]);

        tags.Should().ContainSingle().Which.Id.Should().Be(existing.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_StillDropsABlankName(string name)
    {
        await using var db = CreateContext();

        var tags = await TagResolver.ResolveAsync(db, ["keep", name]);

        tags.Should().ContainSingle().Which.Name.Should().Be("keep");
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
