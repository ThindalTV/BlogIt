using BlogIt.Services;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlogIt.Tests.Unit;

public sealed class PublicationSchedulingServiceTests
{
    [Fact]
    public async Task ProcessDueSchedules_AppliesTransitionsAndPreservesFirstPublication()
    {
        var now = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
        var originalPublication = new DateTime(2025, 4, 10, 0, 0, 0, DateTimeKind.Utc);
        var (service, factory) = CreateService(now);
        Guid publishPostId;
        Guid republishPostId;
        Guid publishPageId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = new AppUser
            {
                Username = "scheduler",
                DisplayName = "Scheduler",
                PasswordHash = "unused"
            };
            var publishPost = CreatePost(author, "publish", isPublished: false);
            publishPost.ScheduledPublishAt = now.UtcDateTime.AddMinutes(-5);
            var republishPost = CreatePost(author, "republish", isPublished: false);
            republishPost.HasBeenPublished = true;
            republishPost.PublishedAt = originalPublication;
            republishPost.ScheduledPublishAt = now.UtcDateTime.AddMinutes(-4);
            republishPost.ScheduledUnpublishAt = now.UtcDateTime.AddMinutes(-3);
            var publishPage = new Page
            {
                Title = "Scheduled page",
                Slug = "scheduled-page",
                Content = "content",
                ScheduledPublishAt = now.UtcDateTime.AddMinutes(-2)
            };

            db.AddRange(publishPost, republishPost, publishPage);
            await db.SaveChangesAsync();
            publishPostId = publishPost.Id;
            republishPostId = republishPost.Id;
            publishPageId = publishPage.Id;
        }

        await service.ProcessDueSchedulesAsync();

        await using var verification = await factory.CreateDbContextAsync();
        var published = await verification.BlogPosts.FindAsync(publishPostId);
        published!.IsPublished.Should().BeTrue();
        published.HasBeenPublished.Should().BeTrue();
        published.PublishedAt.Should().Be(now.UtcDateTime.AddMinutes(-5));
        published.ScheduledPublishAt.Should().BeNull();
        published.UpdatedAt.Should().Be(now.UtcDateTime);

        var republished = await verification.BlogPosts.FindAsync(republishPostId);
        republished!.IsPublished.Should().BeFalse();
        republished.PublishedAt.Should().Be(originalPublication);
        republished.ScheduledPublishAt.Should().BeNull();
        republished.ScheduledUnpublishAt.Should().BeNull();

        var page = await verification.Pages.FindAsync(publishPageId);
        page!.IsPublished.Should().BeTrue();
        page.HasBeenPublished.Should().BeTrue();
        page.ScheduledPublishAt.Should().BeNull();
        page.UpdatedAt.Should().Be(now.UtcDateTime);
    }

    [Fact]
    public async Task ProcessDueSchedules_LeavesFutureTransitionsUntouched()
    {
        var now = new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero);
        var (service, factory) = CreateService(now);
        Guid postId;

        await using (var db = await factory.CreateDbContextAsync())
        {
            var author = new AppUser
            {
                Username = "future",
                DisplayName = "Future",
                PasswordHash = "unused"
            };
            var scheduledPost = CreatePost(author, "future", isPublished: false);
            scheduledPost.ScheduledPublishAt = now.UtcDateTime.AddMinutes(1);
            db.Add(scheduledPost);
            await db.SaveChangesAsync();
            postId = scheduledPost.Id;
        }

        await service.ProcessDueSchedulesAsync();

        await using var verification = await factory.CreateDbContextAsync();
        var post = await verification.BlogPosts.FindAsync(postId);
        post!.IsPublished.Should().BeFalse();
        post.HasBeenPublished.Should().BeFalse();
        post.ScheduledPublishAt.Should().Be(now.UtcDateTime.AddMinutes(1));
    }

    private static BlogPost CreatePost(
        AppUser author,
        string slug,
        bool isPublished) => new()
        {
            Title = slug,
            Slug = slug,
            Summary = slug,
            Content = slug,
            Author = author,
            AuthorId = author.Id,
            IsPublished = isPublished,
            HasBeenPublished = isPublished
        };

    private static (PublicationSchedulingService Service, TestDbContextFactory Factory)
        CreateService(DateTimeOffset now)
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"Scheduling_{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        var timeProvider = new FixedTimeProvider(now);
        var service = new PublicationSchedulingService(
            factory,
            new PreviewTokenService(timeProvider),
            timeProvider,
            NullLogger<PublicationSchedulingService>.Instance);
        return (service, factory);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
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
