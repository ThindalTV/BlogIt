namespace BlogIt.Helpers;

/// <summary>
/// Turns a user's search term into a <c>LIKE</c> pattern for
/// <c>EF.Functions.Like(column, pattern, SearchPattern.EscapeCharacter)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every text search in the engine goes through this rather than <see cref="string.Contains(string)"/>,
/// because <c>Contains</c> only looked case-insensitive. SQL Server translates it to a comparison under
/// the column's collation, and the default collation ignores case, so "blazor" found "Blazor". SQLite
/// translates it to <c>instr()</c>, which compares bytes whatever the collation, so the same search found
/// nothing — and the InMemory provider the tests run on compares ordinally, so the suite could never have
/// noticed. <c>LIKE</c> is case-insensitive on all three: SQL Server through the collation as before,
/// SQLite for ASCII by definition, InMemory by implementation.
/// </para>
/// <para>
/// The term is escaped so that a visitor searching for <c>100%</c> or <c>snake_case</c> gets those
/// characters literally instead of wildcards. <c>[</c> is escaped as well because SQL Server treats it
/// as the start of a character class; SQLite and InMemory accept the escape and match it literally.
/// </para>
/// </remarks>
internal static class SearchPattern
{
    /// <summary>The escape character <see cref="Containing"/> writes; pass it to <c>EF.Functions.Like</c>.</summary>
    internal const string EscapeCharacter = "\\";

    /// <summary>A pattern matching any value that contains <paramref name="term"/> literally.</summary>
    internal static string Containing(string term) =>
        $"%{term
            .Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal)
            .Replace("[", EscapeCharacter + "[", StringComparison.Ordinal)}%";
}
