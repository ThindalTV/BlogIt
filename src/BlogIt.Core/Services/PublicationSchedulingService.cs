using BlogIt.Shared.Data;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Services;

public sealed class PublicationSchedulingService(
    IDbContextFactory<BlogItDbContext> dbContextFactory,
    IPreviewTokenService previewTokens,
    TimeProvider timeProvider,
    ILogger<PublicationSchedulingService> logger) : BackgroundService
{
    public static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval, timeProvider);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueSchedulesAsync(stoppingToken);
                // Piggybacks on this existing timer rather than running its own — see
                // IPreviewTokenService.SweepExpired for why this is needed at all.
                previewTokens.SweepExpired();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process scheduled publication changes; the next poll will retry.");
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task ProcessDueSchedulesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var posts = await db.BlogPosts
            .Where(p =>
                (p.ScheduledPublishAt != null && p.ScheduledPublishAt <= utcNow) ||
                (p.ScheduledUnpublishAt != null && p.ScheduledUnpublishAt <= utcNow))
            .ToListAsync(cancellationToken);

        foreach (var post in posts)
        {
            var changed = false;
            if (post.ScheduledPublishAt <= utcNow)
            {
                if (!post.IsPublished)
                {
                    post.IsPublished = true;
                    post.HasBeenPublished = true;
                    post.PublishedAt ??= post.ScheduledPublishAt;
                }

                post.ScheduledPublishAt = null;
                changed = true;
            }

            if (post.ScheduledUnpublishAt <= utcNow)
            {
                post.IsPublished = false;
                post.ScheduledUnpublishAt = null;
                changed = true;
            }

            if (changed)
                post.UpdatedAt = utcNow;
        }

        var pages = await db.Pages
            .Where(p =>
                (p.ScheduledPublishAt != null && p.ScheduledPublishAt <= utcNow) ||
                (p.ScheduledUnpublishAt != null && p.ScheduledUnpublishAt <= utcNow))
            .ToListAsync(cancellationToken);

        foreach (var page in pages)
        {
            var changed = false;
            if (page.ScheduledPublishAt <= utcNow)
            {
                page.IsPublished = true;
                page.HasBeenPublished = true;
                page.ScheduledPublishAt = null;
                changed = true;
            }

            if (page.ScheduledUnpublishAt <= utcNow)
            {
                page.IsPublished = false;
                page.ScheduledUnpublishAt = null;
                changed = true;
            }

            if (changed)
                page.UpdatedAt = utcNow;
        }

        if (posts.Count > 0 || pages.Count > 0)
            await db.SaveChangesAsync(cancellationToken);
    }
}
