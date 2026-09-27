using BlogIt.Shared.Data;
using BlogIt.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace BlogIt.Api;

/// <summary>
/// Turns a lost update into a <c>409 Conflict</c> the caller can act on, instead of one edit
/// silently overwriting another.
/// </summary>
/// <remarks>
/// Two layers, because they catch different things. <see cref="CheckStamp"/> compares the token the
/// client was given when it loaded the record — that is the case this exists for, a person editing
/// a post for several minutes while someone else saves it. EF Core's own check, surfacing as
/// <see cref="DbUpdateConcurrencyException"/> and handled by <see cref="TrySaveAsync"/>, only
/// covers the narrow race between two requests already in flight, because the API loads and saves
/// inside a single request. Neither layer alone is enough.
/// </remarks>
internal static class ConcurrencyGuard
{
    private const string ConflictMessage =
        "This content was changed by someone else after you loaded it. Reload to see the current version, then reapply your changes.";

    /// <summary>
    /// Returns a conflict result when <paramref name="submitted"/> does not match the entity's
    /// current token, or null when the edit is based on the current version.
    /// </summary>
    /// <remarks>
    /// Fails closed on <see cref="Guid.Empty"/>: a caller that omits the token is treated as out of
    /// date rather than waved through. A silently-skipped check is how the original defect would
    /// come back.
    /// </remarks>
    internal static IResult? CheckStamp(IConcurrencyStamped entity, Guid submitted) =>
        submitted == entity.ConcurrencyStamp ? null : Results.Conflict(ConflictMessage);

    /// <summary>
    /// Saves, converting a concurrency failure — and, when asked, a duplicate-key insert — into a
    /// conflict result. Returns null on success.
    /// </summary>
    /// <param name="duplicateMessage">
    /// The message to answer a duplicate-key insert with, or null to let one propagate.
    /// </param>
    /// <remarks>
    /// The duplicate half closes finding #24: every check-then-act insert in the API — a post or page
    /// slug, a tag, a username — reads to see whether the value is free and then writes it, so the
    /// loser of a race between two such requests had its unique-index violation arrive as an
    /// unhandled <see cref="DbUpdateException"/>. The indexes kept the data correct; only the
    /// response was wrong, a 500 where the surrounding code plainly means 409.
    /// <para>
    /// Opt-in per call site rather than always on, because the catch has to be broad: telling a
    /// unique-index violation apart from any other <see cref="DbUpdateException"/> means reading
    /// provider-specific error numbers, which is both brittle and unreachable on the InMemory
    /// provider the tests run against. Applying it where no insert happens would dress a genuine
    /// fault up as "please try again", so those call sites pass nothing.
    /// </para>
    /// <para>
    /// Both exception shapes are caught for the reason <c>SetupApi</c> documents: a real relational
    /// provider raises <see cref="DbUpdateException"/> for a duplicate key, while EF Core's InMemory
    /// provider raises a bare <see cref="ArgumentException"/>. Handling only the first would make
    /// this untestable.
    /// </para>
    /// </remarks>
    internal static async Task<IResult?> TrySaveAsync(
        this BlogItDbContext db,
        string? duplicateMessage = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        // Ahead of the duplicate clause on purpose: DbUpdateConcurrencyException derives from
        // DbUpdateException, so the other order would retitle every lost update as a duplicate.
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(ConflictMessage);
        }
        catch (Exception ex) when (duplicateMessage is not null && ex is DbUpdateException or ArgumentException)
        {
            return Results.Conflict(duplicateMessage);
        }
    }

    /// <summary>
    /// The answer to an insert that lost a race on a slug or a tag. Retrying works: the value the
    /// loser picked is now visibly taken, so the next attempt either reuses it or steps past it.
    /// </summary>
    internal const string RaceLostMessage =
        "Another request created conflicting content at the same moment. Please try again.";
}
