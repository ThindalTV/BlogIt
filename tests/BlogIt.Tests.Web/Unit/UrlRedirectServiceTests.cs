using BlogIt.Services;
using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

public sealed class UrlRedirectServiceTests
{
    [Fact]
    public async Task ManualRedirectLifecycle_RefreshesCacheAndRejectsConflicts()
    {
        var (service, factory) = CreateService();

        (await service.FindAsync("/missing")).Should().BeNull();

        var created = await service.CreateAsync("/old", "/new", isPermanent: false);

        created.Should().NotBeNull();
        (await service.FindAsync("/old/")).Should().BeEquivalentTo(created);
        (await service.CreateAsync("/old", "/duplicate", true)).Should().BeNull();

        var conflicting = await service.CreateAsync("/occupied", "/target", true);
        var updated = await service.UpdateAsync(
            created!.Id,
            "/renamed",
            "/newer",
            isPermanent: true);

        updated.Should().NotBeNull();
        updated!.SourcePath.Should().Be("/renamed");
        updated.TargetUrl.Should().Be("/newer");
        updated.IsPermanent.Should().BeTrue();
        (await service.FindAsync("/old")).Should().BeNull();
        (await service.FindAsync("/renamed")).Should().BeEquivalentTo(updated);
        (await service.UpdateAsync(created.Id, conflicting!.SourcePath, "/nope", true))
            .Should().BeNull();

        (await service.GetAllAsync()).Select(item => item.SourcePath)
            .Should().Equal("/occupied", "/renamed");
        (await service.DeleteAsync(created.Id)).Should().BeTrue();
        (await service.DeleteAsync(created.Id)).Should().BeFalse();
        (await service.FindAsync("/renamed")).Should().BeNull();

        await using var db = await factory.CreateDbContextAsync();
        (await db.UrlRedirects.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task AutomaticRedirects_UpdateOnlyAutomaticEntries()
    {
        var (service, _) = CreateService();
        var manual = await service.CreateAsync("/manual", "/owner-target", true);

        await service.UpsertAutomaticAsync("/same", "/same");
        await service.UpsertAutomaticAsync("/generated", "/first");
        await service.UpsertAutomaticAsync("/generated", "/second");
        await service.UpsertAutomaticAsync("/manual", "/automatic-target");

        var generated = await service.FindAsync("/generated");
        generated.Should().NotBeNull();
        generated!.TargetUrl.Should().Be("/second");
        generated.IsAutomatic.Should().BeTrue();
        generated.IsPermanent.Should().BeTrue();

        var preservedManual = await service.FindAsync("/manual");
        preservedManual.Should().BeEquivalentTo(manual);
        (await service.FindAsync("/same")).Should().BeNull();
    }

    private static (UrlRedirectService Service, TestDbContextFactory Factory) CreateService()
    {
        var options = new DbContextOptionsBuilder<BlogItDbContext>()
            .UseInMemoryDatabase($"Redirects_{Guid.NewGuid():N}")
            .Options;
        var factory = new TestDbContextFactory(options);
        return (new UrlRedirectService(factory), factory);
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
