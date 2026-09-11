using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BlogIt.Shared.Data;

/// <summary>
/// Forces every <see cref="DateTime"/> BlogIt stores to be a UTC instant, and every one it reads
/// back to carry <see cref="DateTimeKind.Utc"/>.
/// </summary>
/// <remarks>
/// <para>
/// BlogIt writes timestamps with <see cref="DateTime.UtcNow"/>, but <c>datetime2</c> has no offset,
/// so EF materialises every value as <see cref="DateTimeKind.Unspecified"/>. That is the silent half
/// of the problem: the values are correct instants wearing the wrong label. The loud half is what
/// happens next — <see cref="DateTimeOffset"/> conversion reads an <c>Unspecified</c> kind as
/// <em>local time</em>, so on any server not running UTC every timestamp the public DTOs expose
/// would shift by the server's offset. Normalising here makes that conversion correct by
/// construction rather than by remembering to call a helper.
/// </para>
/// <para>
/// The write half matters too: a client posting a <c>Local</c>-kinded scheduled time previously
/// stored local wall-clock as though it were UTC.
/// </para>
/// <para>
/// <strong>Constraint.</strong> EF cannot translate member access on a value-converted property —
/// <c>.Year</c>, <c>.Month</c>, <c>.Date</c>, <c>.AddDays(...)</c> and friends will throw at
/// translation time rather than falling back silently. Filter and group by <em>ranges</em> of whole
/// <see cref="DateTime"/> values instead; see
/// <c>PublicContentService.GetPostsByDateRangeAsync</c>, which is written that way for this reason,
/// and <c>GetArchiveCountsAsync</c>, which groups client-side for the same one.
/// </para>
/// </remarks>
internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}
