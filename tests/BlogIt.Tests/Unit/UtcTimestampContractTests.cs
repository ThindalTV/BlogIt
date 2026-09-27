using System.Collections;
using System.Reflection;
using System.Text.Json;
using BlogIt.Services;
using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Guards BlogIt's timestamp contract: every stored <see cref="DateTime"/> is a UTC instant, and
/// every timestamp a public DTO exposes is a <see cref="DateTimeOffset"/> at offset zero.
/// </summary>
/// <remarks>
/// <para>
/// The defect this replaces was silent in both directions. <c>datetime2</c> carries no offset, so
/// EF materialised every value as <see cref="DateTimeKind.Unspecified"/> — correct instants wearing
/// the wrong label. Formatting one with <c>"o"</c> then produced a string with no offset at all,
/// which Open Graph's <c>article:published_time</c> rejects, and serialising one to JSON produced a
/// timestamp with no <c>Z</c> that clients read as local time. Nothing threw; the output was just
/// wrong.
/// </para>
/// <para>
/// Both tests are written as rules driven by reflection rather than as lists of members, so a
/// timestamp added to an entity or a DTO tomorrow is covered the moment it exists rather than the
/// next time somebody remembers this file.
/// </para>
/// </remarks>
public class UtcTimestampContractTests
{
    [Fact]
    public void EveryDateTimeColumn_IsStoredAndReadAsUtc()
    {
        using var context = new BlogItDbContext(
            new DbContextOptionsBuilder<BlogItDbContext>()
                .UseInMemoryDatabase($"UtcModel_{Guid.NewGuid():N}")
                .Options);

        var unconverted = context.Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property =>
                (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTime))
            .Where(property => property.GetValueConverter() is not UtcDateTimeConverter)
            .Select(property => $"{property.DeclaringType.ShortName()}.{property.Name}")
            .ToList();

        unconverted.Should().BeEmpty(
            "every DateTime column must round-trip through UtcDateTimeConverter, or it comes back "
            + "Unspecified and converting it to DateTimeOffset silently reads it as local time");
    }

    [Fact]
    public async Task EveryTimestampOnThePublicReadSurface_ComesBackAtOffsetZero()
    {
        var factory = new TestDbContextFactory(
            new DbContextOptionsBuilder<BlogItDbContext>()
                .UseInMemoryDatabase($"UtcSurface_{Guid.NewGuid():N}")
                .Options);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = new AppUser
            {
                Username = "author",
                DisplayName = "Author",
                PasswordHash = "unused"
            };
            db.BlogPosts.Add(new BlogPost
            {
                Title = "Live",
                Slug = "live",
                Summary = "Summary",
                Content = "Body text",
                IsPublished = true,
                HasBeenPublished = true,
                PublishedAt = new DateTime(2025, 3, 15, 9, 30, 0, DateTimeKind.Utc),
                // Deliberately Unspecified — the kind EF hands back for a value written before the
                // converter existed, and the one whose conversion would drift by the server offset.
                ScheduledUnpublishAt = new DateTime(2025, 4, 1, 0, 0, 0),
                Author = author,
                Tags = [new Tag { Name = "Azure", Slug = "azure" }]
            });
            db.Pages.Add(new Page
            {
                Title = "About",
                Slug = "about",
                Content = "About us",
                IsPublished = true,
                HasBeenPublished = true
            });
            await db.SaveChangesAsync();
        }

        var service = new PublicContentService(factory);

        // Every read path, including SearchPostsAsync — the one that projects by hand instead of
        // loading entities, and so is the one a mapper-level fix would have missed.
        AssertUtc(await service.GetRecentPostsAsync(10), nameof(IPublicContentService.GetRecentPostsAsync));
        AssertUtc(await service.GetPostsAsync(1, 10), nameof(IPublicContentService.GetPostsAsync));
        AssertUtc(await service.SearchPostsAsync("Live", 1, 10), nameof(IPublicContentService.SearchPostsAsync));
        AssertUtc(await service.GetPostsByTagAsync("azure", 1, 10), nameof(IPublicContentService.GetPostsByTagAsync));
        AssertUtc(
            await service.GetPostsByDateRangeAsync(
                new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                1,
                10),
            nameof(IPublicContentService.GetPostsByDateRangeAsync));
        AssertUtc(await service.GetPostAsync("live", includeNavigation: true), nameof(IPublicContentService.GetPostAsync));
        AssertUtc(await service.GetPageAsync("about"), nameof(IPublicContentService.GetPageAsync));

        var post = (await service.GetPostsAsync(1, 10)).Posts.Single();

        // The assertion that the reflection walk cannot make: that a timestamp actually reached the
        // wire with an offset on it. This is the admin clients' bug — they parsed an offset-less
        // string as local time.
        JsonSerializer.Serialize(post).Should().Contain("+00:00");

        // And that the values are the instants that were stored, not merely well-labelled.
        post.PublishedAt.Should().Be(new DateTimeOffset(2025, 3, 15, 9, 30, 0, TimeSpan.Zero));
        post.ScheduledUnpublishAt.Should().Be(new DateTimeOffset(2025, 4, 1, 0, 0, 0, TimeSpan.Zero));
    }

    /// <summary>
    /// Walks anything the read surface returns and asserts every timestamp it reaches is at offset
    /// zero, failing if it found none at all.
    /// </summary>
    /// <remarks>
    /// The "found none" half matters as much as the check: without it, a DTO whose timestamps were
    /// renamed or wrapped would silently stop being covered while the test kept passing.
    /// </remarks>
    private static void AssertUtc(object? value, string source)
    {
        var found = 0;
        Walk(value, source, ref found);
        found.Should().BeGreaterThan(
            0, $"{source} should expose at least one timestamp for this test to be meaningful");
    }

    private static void Walk(object? value, string path, ref int found)
    {
        switch (value)
        {
            case null:
                return;
            case DateTimeOffset instant:
                instant.Offset.Should().Be(
                    TimeSpan.Zero,
                    "{0} must be a UTC instant, not a local-offset reading",
                    path);
                found++;
                return;
            case DateTime naked:
                throw new InvalidOperationException(
                    $"{path} exposes a bare DateTime ({naked:o}). The public read surface uses "
                    + "DateTimeOffset so that formatting and serialising it cannot lose the offset.");
            case string or decimal or Guid:
                return;
            case IEnumerable sequence:
                var index = 0;
                foreach (var item in sequence)
                    Walk(item, $"{path}[{index++}]", ref found);
                return;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("System") == true)
            return;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;

            Walk(property.GetValue(value), $"{path}.{property.Name}", ref found);
        }
    }

    private sealed class TestDbContextFactory(DbContextOptions<BlogItDbContext> options)
        : IDbContextFactory<BlogItDbContext>
    {
        public BlogItDbContext CreateDbContext() => new(options);

        public Task<BlogItDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
