using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using BlogIt.Web.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

public class SettingsServiceTests
{
    private sealed class TestDbContextFactory(
        DbContextOptions<BlogItDbContext> options) : IDbContextFactory<BlogItDbContext>
    {
        public BlogItDbContext CreateDbContext() => new(options);
    }

    private static (BlogItDbContext Db, SettingsService Service) CreateSubject()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return (new BlogItDbContext(options), new SettingsService(new TestDbContextFactory(options)));
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenKeyMissing()
    {
        var (_, svc) = CreateSubject();
        (await svc.GetAsync("nonexistent")).Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_PersistsValue()
    {
        var (_, svc) = CreateSubject();
        await svc.SetAsync("foo", "bar");
        (await svc.GetAsync("foo")).Should().Be("bar");
    }

    [Fact]
    public async Task SetAsync_UpdatesExistingValue()
    {
        var (db, svc) = CreateSubject();
        db.SiteSettings.Add(new SiteSetting { Key = "x", Value = "old" });
        await db.SaveChangesAsync();
        await svc.SetAsync("x", "new");
        (await svc.GetAsync("x")).Should().Be("new");
    }

    [Fact]
    public async Task SetManyAsync_PersistsMultipleValues()
    {
        var (_, svc) = CreateSubject();
        await svc.SetManyAsync(new() { ["a"] = "1", ["b"] = "2" });
        (await svc.GetAsync("a")).Should().Be("1");
        (await svc.GetAsync("b")).Should().Be("2");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllSettings()
    {
        var (db, svc) = CreateSubject();
        db.SiteSettings.AddRange(
            new SiteSetting { Key = "k1", Value = "v1" },
            new SiteSetting { Key = "k2", Value = "v2" });
        await db.SaveChangesAsync();
        var all = await svc.GetAllAsync();
        all.Should().ContainKey("k1").WhoseValue.Should().Be("v1");
        all.Should().ContainKey("k2").WhoseValue.Should().Be("v2");
    }
}
