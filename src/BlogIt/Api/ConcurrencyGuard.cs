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
    /// Saves, converting a concurrency failure into a conflict result. Returns null on success.
    /// </summary>
    internal static async Task<IResult?> TrySaveAsync(
        this BlogItDbContext db,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(ConflictMessage);
        }
    }
}
