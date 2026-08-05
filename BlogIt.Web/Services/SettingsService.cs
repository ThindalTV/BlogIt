using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Web.Services;

public class SettingsService(IDbContextFactory<BlogItDbContext> dbContextFactory) : ISettingsService
{
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private volatile IReadOnlyDictionary<string, string>? cache;

    public async Task<string?> GetAsync(string key)
    {
        var settings = await GetCacheAsync();
        return settings.GetValueOrDefault(key);
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        return new Dictionary<string, string>(await GetCacheAsync());
    }

    public async Task SetAsync(string key, string value)
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
            var updated = new Dictionary<string, string>(await LoadCacheAsync(db))
            {
                [key] = value
            };
            cache = updated;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task SetManyAsync(Dictionary<string, string> settings)
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
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> GetCacheAsync()
    {
        if (cache is not null)
            return cache;

        await cacheLock.WaitAsync();
        try
        {
            if (cache is null)
            {
                await using var db = await dbContextFactory.CreateDbContextAsync();
                cache = await LoadCacheAsync(db);
            }

            return cache;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private static async Task<Dictionary<string, string>> LoadCacheAsync(BlogItDbContext db) =>
        await db.SiteSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
}
