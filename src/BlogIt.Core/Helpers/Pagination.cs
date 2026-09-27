namespace BlogIt.Shared.Helpers;

/// <summary>
/// Forces the <c>page</c> and <c>pageSize</c> query parameters of the admin list endpoints into a
/// range the database can actually be asked for.
/// </summary>
/// <remarks>
/// These arrive straight off the query string. <c>?page=0</c> reached EF as <c>Skip(-20)</c>, which
/// SQL Server rejects outright — an unhandled exception, so a 500 from a URL anyone with an admin
/// session could mistype. And <c>pageSize</c> was honoured whatever it said, while the admin
/// listings materialize whole entities (a post brings its <c>nvarchar(max)</c> body along with its
/// tags and author), so one request could ask the server to load the entire table into memory.
/// </remarks>
public static class Pagination
{
    /// <summary>
    /// Largest page a client may ask for. Well clear of the 20 the admin clients actually request
    /// and the 1–5 the dashboard tiles use, so it is a ceiling on abuse rather than a limit anyone
    /// meets while using the product.
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>Returns <paramref name="page"/> and <paramref name="pageSize"/> forced into range.</summary>
    /// <remarks>
    /// A page past the end of the data is left as asked rather than snapped down to the last one,
    /// which is where this differs from <c>PublicContentService</c>: the public site renders
    /// whatever page it is handed, so snapping is the only way to avoid a blank archive page,
    /// whereas an admin client drives its pager off <c>PagedResult.TotalCount</c> and an empty page
    /// is the honest answer to "give me page 900". Snapping here would also mean counting rows
    /// before every query.
    /// </remarks>
    public static (int Page, int PageSize) Clamp(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}
