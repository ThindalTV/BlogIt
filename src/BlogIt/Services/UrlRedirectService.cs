using BlogIt.Shared.Data;
using BlogIt.Shared.DTOs;
using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Services;

public interface IUrlRedirectService
{
    Task<UrlRedirectDto?> FindAsync(string sourcePath);
    Task<IReadOnlyList<UrlRedirectDto>> GetAllAsync();
    Task<UrlRedirectDto?> CreateAsync(string sourcePath, string targetUrl, bool isPermanent);
    Task<UrlRedirectDto?> UpdateAsync(Guid id, string sourcePath, string targetUrl, bool isPermanent);
    Task<bool> DeleteAsync(Guid id);
    Task UpsertAutomaticAsync(string sourcePath, string targetUrl);
}

public sealed class UrlRedirectService(IDbContextFactory<BlogItDbContext> dbContextFactory)
    : IUrlRedirectService
{
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private volatile IReadOnlyDictionary<string, UrlRedirectDto>? cache;

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
            return true;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    public async Task UpsertAutomaticAsync(string sourcePath, string targetUrl)
    {
        if (sourcePath.Equals(targetUrl, StringComparison.OrdinalIgnoreCase))
            return;

        await cacheLock.WaitAsync();
        try
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var entity = await db.UrlRedirects
                .FirstOrDefaultAsync(item => item.SourcePath == sourcePath);
            if (entity is null)
            {
                db.UrlRedirects.Add(new UrlRedirect
                {
                    SourcePath = sourcePath,
                    TargetUrl = targetUrl,
                    IsPermanent = true,
                    IsAutomatic = true
                });
            }
            else if (entity.IsAutomatic)
            {
                entity.TargetUrl = targetUrl;
                entity.IsPermanent = true;
                entity.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync();
            cache = await LoadCacheAsync(db);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, UrlRedirectDto>> GetCacheAsync()
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
        item.CreatedAt,
        item.UpdatedAt);
}
