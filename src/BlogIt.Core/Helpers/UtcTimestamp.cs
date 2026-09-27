namespace BlogIt.Shared.Helpers;

/// <summary>
/// Converts the <see cref="DateTime"/> values entities store into the
/// <see cref="DateTimeOffset"/> values the public DTOs expose.
/// </summary>
/// <remarks>
/// <para>
/// Every mapper goes through here rather than leaning on the implicit
/// <see cref="DateTime"/>-to-<see cref="DateTimeOffset"/> conversion, because that conversion reads
/// a <see cref="DateTimeKind.Unspecified"/> value as <em>local time</em> and would silently shift
/// the instant on any server not running UTC. <c>UtcDateTimeConverter</c> makes stored values
/// arrive UTC-kinded so the conversion is correct, but relying on it implicitly would leave the
/// mapping quietly wrong under any provider that skips value converters, and would give a reader no
/// hint that a conversion with a failure mode is happening at all.
/// </para>
/// <para>
/// BlogIt writes every timestamp as UTC, so a value arriving <c>Unspecified</c> is a correct instant
/// wearing the wrong label and is read as UTC. A <c>Local</c> value is converted.
/// </para>
/// </remarks>
public static class UtcTimestamp
{
    /// <summary>The instant <paramref name="value"/> names, at offset zero.</summary>
    public static DateTimeOffset ToOffset(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => new DateTimeOffset(value),
        DateTimeKind.Local => new DateTimeOffset(value.ToUniversalTime()),
        _ => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
    };

    /// <inheritdoc cref="ToOffset(DateTime)"/>
    public static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? ToOffset(value.Value) : null;

    /// <summary>
    /// The UTC <see cref="DateTime"/> to store for <paramref name="value"/>.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="ToOffset(DateTime)"/>, for the write path: a client may send any
    /// offset, and entities store UTC.
    /// </remarks>
    public static DateTime ToStorage(DateTimeOffset value) => value.UtcDateTime;

    /// <inheritdoc cref="ToStorage(DateTimeOffset)"/>
    public static DateTime? ToStorage(DateTimeOffset? value) =>
        value.HasValue ? value.Value.UtcDateTime : null;
}
