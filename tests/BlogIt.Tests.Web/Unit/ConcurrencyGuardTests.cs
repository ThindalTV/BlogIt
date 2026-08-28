using BlogIt.Api;
using BlogIt.Shared.Data;
using BlogIt.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The save-side half of finding #24: which failures <c>ConcurrencyGuard.TrySaveAsync</c> converts
/// into a 409 and, just as importantly, which it leaves alone.
/// </summary>
/// <remarks>
/// Unit-level rather than through HTTP because the interesting case is a failure that must
/// <em>not</em> be converted, and TestServer's handling of an exception escaping the pipeline is not
/// something these assertions should depend on.
/// </remarks>
public class ConcurrencyGuardTests
{
    private const string DuplicateMessage = "Try again.";

    [Fact]
    public async Task TrySaveAsync_OnSuccess_ReturnsNull()
    {
        await using var db = Context();

        (await db.TrySaveAsync(DuplicateMessage)).Should().BeNull();
    }

    [Fact]
    public async Task TrySaveAsync_WithADuplicateMessage_ConvertsARelationalDuplicateKeyToConflict()
    {
        await using var db = Context(SaveFailureSwitch.DuplicateKeyOnRelationalProvider());

        var result = await db.TrySaveAsync(DuplicateMessage);

        result.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        result.Should().BeAssignableTo<IValueHttpResult>()
            .Which.Value.Should().Be(DuplicateMessage);
    }

    [Fact]
    public async Task TrySaveAsync_WithADuplicateMessage_ConvertsTheInMemoryProvidersShapeToo()
    {
        // The provider divergence SetupApi documents: InMemory throws a bare ArgumentException for a
        // duplicate key where SQL Server throws DbUpdateException. Handling one and not the other
        // would leave the fix untestable.
        await using var db = Context(SaveFailureSwitch.DuplicateKeyOnInMemoryProvider());

        var result = await db.TrySaveAsync(DuplicateMessage);

        result.Should().BeAssignableTo<IValueHttpResult>().Which.Value.Should().Be(DuplicateMessage);
    }

    [Fact]
    public async Task TrySaveAsync_WithoutADuplicateMessage_LeavesADuplicateKeyFailureAlone()
    {
        // Opt-in per call site on purpose. The catch has to be broad — telling a unique-index
        // violation apart from any other DbUpdateException needs provider-specific error numbers —
        // so the endpoints that cannot produce one must not have it applied to them, or a genuine
        // fault at those call sites would be reported to the caller as "please try again".
        await using var db = Context(SaveFailureSwitch.DuplicateKeyOnRelationalProvider());

        var act = () => db.TrySaveAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TrySaveAsync_PrefersTheConcurrencyMessage_ForALostUpdate()
    {
        // DbUpdateConcurrencyException derives from DbUpdateException, so the two catch clauses are
        // order-dependent and the wrong order silently retitles every lost update as a duplicate.
        await using var db = Context(new DbUpdateConcurrencyException("stale"));

        var result = await db.TrySaveAsync(DuplicateMessage);

        result.Should().BeAssignableTo<IValueHttpResult>()
            .Which.Value.Should().BeOfType<string>()
            .Which.Should().Contain("changed by someone else");
    }

    private static FailableDbContext Context(Exception? failure = null) =>
        new(new DbContextOptionsBuilder<BlogItDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new SaveFailureSwitch { NextFailure = failure });
}
