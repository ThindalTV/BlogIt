namespace BlogIt.Shared;

/// <summary>
/// Maximum lengths for the required text columns: titles, and the two fields on an account.
/// </summary>
/// <remarks>
/// Same reasoning as <see cref="SeoLimits"/> — one source of truth for a number that is load-bearing
/// in two places, the EF column width and the boundary check that returns a 400 instead of letting
/// the value fail on <c>SaveChanges</c> as an unhandled database error.
/// <para>
/// Unlike the SEO limits these are not arbitrary-but-generous: they are the widths the schema has
/// always declared, lifted out of <c>BlogItDbContext</c> unchanged so no existing row can become
/// invalid. Nothing here bounds the unbounded columns — a post's summary and a page's content stay
/// <c>nvarchar(max)</c>, where the only thing the database refuses is null.
/// </para>
/// </remarks>
public static class ContentLimits
{
    /// <summary>
    /// Titles on posts, pages, media files and AI conversations. One constant for all four because
    /// the schema gives all four the same width, and a title is a title.
    /// </summary>
    public const int TitleLength = 500;

    /// <summary>Login name on an account. Also the key of that table's unique index.</summary>
    public const int UsernameLength = 100;

    /// <summary>Author name shown next to posts and uploads.</summary>
    public const int DisplayNameLength = 200;
}
