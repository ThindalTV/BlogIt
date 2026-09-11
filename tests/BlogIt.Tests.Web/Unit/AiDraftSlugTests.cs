using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The third caller in finding #23. Exporting a brainstorm to a draft derives the post's slug from
/// the title the model wrote, which is as likely to be non-Latin as any other title, and the rest of
/// <c>ExportToDraftAsync</c> cannot be reached in a test without a live provider — hence the seam.
/// </summary>
public class AiDraftSlugTests
{
    [Fact]
    public async Task NextDraftSlugAsync_FallsBackForANonLatinTitle()
    {
        await using var db = Context();

        var slug = await OpenAiService.NextDraftSlugAsync(db, "Привет мир");

        slug.Should().Be(SlugHelper.SlugifyOrFallback("Привет мир"));
        slug.Should().NotBeEmpty();
    }

    [Fact]
    public async Task NextDraftSlugAsync_AvoidsASlugAlreadyTaken()
    {
        await using var db = Context();
        db.BlogPosts.Add(Post(SlugHelper.SlugifyOrFallback("Привет мир")));
        await db.SaveChangesAsync();

        var slug = await OpenAiService.NextDraftSlugAsync(db, "Привет мир");

        slug.Should().Be($"{SlugHelper.SlugifyOrFallback("Привет мир")}-2");
    }

    [Fact]
    public async Task NextDraftSlugAsync_CountsPastAnAlreadySuffixedSlug()
    {
        // Proves the collision query returns the suffixed rows and not only the exact one; with just
        // the base slug taken, both a correct and an over-narrow query answer "-2".
        await using var db = Context();
        var fallback = SlugHelper.SlugifyOrFallback("Привет мир");
        db.BlogPosts.Add(Post(fallback));
        db.BlogPosts.Add(Post($"{fallback}-2"));
        await db.SaveChangesAsync();

        var slug = await OpenAiService.NextDraftSlugAsync(db, "Привет мир");

        slug.Should().Be($"{fallback}-3");
    }

    [Fact]
    public async Task NextDraftSlugAsync_KeepsAMaximumLengthTitleInsideTheColumn()
    {
        await using var db = Context();
        var title = new string('a', ContentLimits.TitleLength);
        db.BlogPosts.Add(Post(title));
        await db.SaveChangesAsync();

        var slug = await OpenAiService.NextDraftSlugAsync(db, title);

        slug.Length.Should().BeLessThanOrEqualTo(ContentLimits.SlugLength);
        slug.Should().NotBe(title);
    }

    private static BlogPost Post(string slug) => new()
    {
        Title = "Taken",
        Slug = slug,
        Summary = "s",
        AuthorId = Guid.NewGuid()
    };

    private static BlogItDbContext Context() =>
        new(new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
