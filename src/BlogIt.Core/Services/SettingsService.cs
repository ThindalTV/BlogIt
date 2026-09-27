using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Services;

public class SettingsService : ISettingsService
{
    /// <summary>
    /// How long a cached settings snapshot is served before it is reloaded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The cache used to have no expiry at all: it was refreshed only by the instance that wrote a
    /// setting, so a change made on one instance was invisible to every other one until it
    /// restarted. That is most of BlogIt's documented multi-instance breakage, and the sharpest case
    /// was rotating the JWT secret — <c>JwtSigningKeyCache</c> reads it through this service, so
    /// other instances kept validating against the old key and signed-in admins were logged out at
    /// random depending on which instance answered.
    /// </para>
    /// <para>
    /// A short expiry does not make BlogIt multi-instance — preview links and the publication
    /// scheduler are still process-local — but it bounds the damage to seconds instead of forever,
    /// for the cost of one small query per instance per interval. Writes still refresh eagerly, so
    /// a single instance never waits for this.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly IDbContextFactory<BlogItDbContext> dbContextFactory;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private volatile IReadOnlyDictionary<string, string?>? cache;
    private long cacheLoadedAt;

    public SettingsService(IDbContextFactory<BlogItDbContext> dbContextFactory)
        : this(dbContextFactory, TimeProvider.System)
    {
    }

    /// <param name="timeProvider">
    /// Clock used to expire the cache. Defaulted through the other constructor so that a host
    /// constructing this directly keeps compiling.
    /// </param>
    public SettingsService(
        IDbContextFactory<BlogItDbContext> dbContextFactory,
        TimeProvider timeProvider)
    {
        this.dbContextFactory = dbContextFactory;
        this.timeProvider = timeProvider;
    }

    public async Task<string?> GetAsync(string key)
    {
        var settings = await GetCacheAsync();
        return settings.GetValueOrDefault(key);
    }

    public async Task<Dictionary<string, string?>> GetAllAsync()
    {
        return new Dictionary<string, string?>(await GetCacheAsync());
    }

    public async Task SetAsync(string key, string? value)
    {
        await cacheLock.WaitAsync();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var existing = await db.SiteSettings.FindAsync(key);
            if (existing is null)
                db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
            else
                existing.Value = value;

            await db.SaveChangesAsync();
            var updated = new Dictionary<string, string?>(await LoadCacheAsync(db))
            {
                [key] = value
            };
            cache = updated;
            MarkLoaded();
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task SetManyAsync(Dictionary<string, string?> settings)
    {
        await cacheLock.WaitAsync();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            foreach (var (key, value) in settings)
            {
                var existing = await db.SiteSettings.FindAsync(key);
                if (existing is null)
                    db.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
                else
                    existing.Value = value;
            }

            await db.SaveChangesAsync();
            cache = await LoadCacheAsync(db);
            MarkLoaded();
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, string?>> GetCacheAsync()
    {
        var snapshot = cache;
        if (snapshot is not null && !IsStale())
            return snapshot;

        await cacheLock.WaitAsync();
        try
        {
            // Re-checked inside the lock: several requests can see the same expiry at once, and only
            // the first of them should pay for the reload.
            if (cache is null || IsStale())
            {
                await using var db = await dbContextFactory.CreateDbContextAsync();
                cache = await LoadCacheAsync(db);
                MarkLoaded();
            }

            return cache;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private bool IsStale() =>
        timeProvider.GetElapsedTime(Interlocked.Read(ref cacheLoadedAt)) > CacheLifetime;

    private void MarkLoaded() =>
        Interlocked.Exchange(ref cacheLoadedAt, timeProvider.GetTimestamp());

    private static async Task<Dictionary<string, string?>> LoadCacheAsync(BlogItDbContext db) =>
        await db.SiteSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
}
