using BlogIt.Services;
using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Covers the expiry on the settings and redirect caches — the fix for most of BlogIt's
/// multi-instance breakage.
/// </summary>
/// <remarks>
/// <para>
/// Both caches were whole-table snapshots with no expiry, refreshed only by the instance that wrote
/// them. A second instance therefore never saw a setting change, a redirect edit, or a rotated JWT
/// secret until it restarted. The JWT case was the sharpest: <c>JwtSigningKeyCache</c> reads the
/// secret through <see cref="ISettingsService"/>, so after a rotation each instance rejected the
/// other's tokens and admins were signed out depending on which one answered.
/// </para>
/// <para>
/// These tests use two service instances over one database, which is what two application instances
/// look like from the cache's point of view.
/// </para>
/// </remarks>
public class CacheExpiryTests
{
    [Fact]
    public async Task ASettingWrittenByOneInstance_BecomesVisibleToAnotherAfterTheCacheExpires()
    {
        var factory = CreateFactory();
        var clock = new ManualTimeProvider();
        var writer = new SettingsService(factory, clock);
        var reader = new SettingsService(factory, clock);

        await writer.SetAsync(SettingKeys.SiteName, "Original");
        (await reader.GetAsync(SettingKeys.SiteName)).Should().Be("Original");

        await writer.SetAsync(SettingKeys.SiteName, "Renamed");

        // Still inside the cache lifetime: the reader is entitled to be stale, and this is the
        // behaviour that keeps a single-instance site from re-querying on every settings read.
        (await reader.GetAsync(SettingKeys.SiteName)).Should().Be("Original");

        clock.Advance(TimeSpan.FromSeconds(31));

        // Before the expiry existed, this stayed "Original" until the process restarted.
        (await reader.GetAsync(SettingKeys.SiteName)).Should().Be("Renamed");
    }

    [Fact]
    public async Task AWritingInstance_SeesItsOwnChangeImmediately()
    {
        var factory = CreateFactory();
        var clock = new ManualTimeProvider();
        var settings = new SettingsService(factory, clock);

        await settings.SetAsync(SettingKeys.SiteName, "First");
        await settings.SetAsync(SettingKeys.SiteName, "Second");

        // Writes still refresh eagerly, so adding an expiry did not make a single-instance site
        // wait for its own edits.
        (await settings.GetAsync(SettingKeys.SiteName)).Should().Be("Second");
    }

    [Fact]
    public async Task ARedirectAddedByOneInstance_BecomesVisibleToAnotherAfterTheCacheExpires()
    {
        var factory = CreateFactory();
        var clock = new ManualTimeProvider();
        var writer = new UrlRedirectService(factory, clock);
        var reader = new UrlRedirectService(factory, clock);

        // Populates the reader's cache before the write, so it has something stale to hold on to.
        (await reader.FindAsync("/old")).Should().BeNull();

        await writer.CreateAsync("/old", "https://example.com/new", isPermanent: true);

        (await reader.FindAsync("/old")).Should().BeNull();

        clock.Advance(TimeSpan.FromSeconds(31));

        (await reader.FindAsync("/old"))!.TargetUrl.Should().Be("https://example.com/new");
    }

    private static TestDbContextFactory CreateFactory() => new(
        new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"CacheExpiry_{Guid.NewGuid():N}")
            .Options);

    /// <summary>A clock that only moves when a test moves it.</summary>
    /// <remarks>
    /// Hand-rolled rather than pulled in from a testing package, so the cache lifetime can be
    /// crossed deterministically without adding a dependency for one type.
    /// </remarks>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private long timestamp = 1_000_000;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan amount) => timestamp += amount.Ticks;
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
