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

    private static BlogItDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new BlogItDbContext(options);
    }
}
