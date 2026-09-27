using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using BlogIt.Shared.Helpers;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Services;

public interface IUrlRedirectService
{
    Task<UrlRedirectDto?> FindAsync(string sourcePath);
    Task<IReadOnlyList<UrlRedirectDto>> GetAllAsync();
    Task<UrlRedirectDto?> CreateAsync(string sourcePath, string targetUrl, bool isPermanent);
    Task<UrlRedirectDto?> UpdateAsync(Guid id, string sourcePath, string targetUrl, bool isPermanent);
    Task<bool> DeleteAsync(Guid id);
}

public sealed class UrlRedirectService : IUrlRedirectService
{
    /// <summary>
    /// How long a cached redirect table is served before it is reloaded.
    /// </summary>
    /// <remarks>
    /// Same reasoning as <see cref="SettingsService"/>: the cache was previously refreshed only by
    /// the instance that wrote it, so a redirect added on one instance never took effect on any
    /// other. Matches that service's interval so the two behave alike.
    /// </remarks>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(30);

    private readonly IDbContextFactory<BlogItDbContext> dbContextFactory;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private volatile IReadOnlyDictionary<string, UrlRedirectDto>? cache;
    private long cacheLoadedAt;

    public UrlRedirectService(IDbContextFactory<BlogItDbContext> dbContextFactory)
        : this(dbContextFactory, TimeProvider.System)
    {
    }

    /// <param name="timeProvider">Clock used to expire the cache.</param>
    public UrlRedirectService(
        IDbContextFactory<BlogItDbContext> dbContextFactory,
        TimeProvider timeProvider)
    {
        this.dbContextFactory = dbContextFactory;
        this.timeProvider = timeProvider;
    }

    public async Task<UrlRedirectDto?> FindAsync(string sourcePath)
    {
        var normalizedPath = sourcePath.Length > 1
            ? sourcePath.TrimEnd('/')
            : sourcePath;
        return (await GetCacheAsync()).GetValueOrDefault(normalizedPath);
    }

    public async Task<IReadOnlyList<UrlRedirectDto>> GetAllAsync() =>
        (await GetCacheAsync()).Values
            .OrderBy(item => item.SourcePath)
            .ToList();

    public async Task<UrlRedirectDto?> CreateAsync(
        string sourcePath,
        string targetUrl,
        bool isPermanent)
    {
        await cacheLock.WaitAsync();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            if (await db.UrlRedirects.AnyAsync(item => item.SourcePath == sourcePath))
                return null;

            var entity = new UrlRedirect
            {
                SourcePath = sourcePath,
                TargetUrl = targetUrl,
                IsPermanent = isPermanent,
                IsAutomatic = false
            };
            db.UrlRedirects.Add(entity);
            await db.SaveChangesAsync();
            cache = await LoadCacheAsync(db);
            MarkLoaded();
            return ToDto(entity);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task<UrlRedirectDto?> UpdateAsync(
        Guid id,
        string sourcePath,
        string targetUrl,
        bool isPermanent)
    {
        await cacheLock.WaitAsync();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var entity = await db.UrlRedirects.FindAsync(id);
            if (entity is null
                || await db.UrlRedirects.AnyAsync(item => item.Id != id && item.SourcePath == sourcePath))
            {
                return null;
            }

            entity.SourcePath = sourcePath;
            entity.TargetUrl = targetUrl;
            entity.IsPermanent = isPermanent;
            entity.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            cache = await LoadCacheAsync(db);
            MarkLoaded();
            return ToDto(entity);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await cacheLock.WaitAsync();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var entity = await db.UrlRedirects.FindAsync(id);
            if (entity is null)
                return false;

            db.UrlRedirects.Remove(entity);
            await db.SaveChangesAsync();
            cache = await LoadCacheAsync(db);
            MarkLoaded();
            return true;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, UrlRedirectDto>> GetCacheAsync()
    {
        var snapshot = cache;
        if (snapshot is not null && !IsStale())
            return snapshot;

        await cacheLock.WaitAsync();
        try
        {
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

    private static async Task<Dictionary<string, UrlRedirectDto>> LoadCacheAsync(
        BlogItDbContext db)
    {
        var redirects = await db.UrlRedirects
            .AsNoTracking()
            .ToListAsync();
        return redirects
            .Select(ToDto)
            .ToDictionary(item => item.SourcePath, StringComparer.OrdinalIgnoreCase);
    }

    private static UrlRedirectDto ToDto(UrlRedirect item) => new(
        item.Id,
        item.SourcePath,
        item.TargetUrl,
        item.IsPermanent,
        item.IsAutomatic,
        UtcTimestamp.ToOffset(item.CreatedAt),
        UtcTimestamp.ToOffset(item.UpdatedAt));
}
