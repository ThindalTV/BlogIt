namespace BlogIt.Admin.Services;

/// <summary>
/// Page-number arithmetic for the shared <c>Pager</c> component. Plain static code rather than
/// component logic so the awkward cases — a partial last page, a current page pinned to either end,
/// a total that outgrows the button strip — are unit testable without rendering.
/// </summary>
/// <remarks>Public for the same reason as <see cref="AdminLoginRedirect"/> — see the note there.</remarks>
public static class AdminPageWindow
{
    /// <summary>
    /// Widest strip of page buttons rendered at once. Odd, so the current page sits in the middle
    /// with an equal number of neighbours either side. The lists used to render one button per page,
    /// which was fine at three pages and unusable at three hundred.
    /// </summary>
    public const int MaxButtons = 7;

    /// <summary>Number of pages <paramref name="totalCount"/> rows fill, counting a partial last one.</summary>
    public static int TotalPages(int totalCount, int pageSize)
    {
        if (totalCount <= 0)
            return 0;

        return (int)Math.Ceiling(totalCount / (double)Math.Max(1, pageSize));
    }

    /// <summary>Forces a page request into <c>1..totalPages</c>, never below 1.</summary>
    public static int Clamp(int page, int totalPages) =>
        Math.Clamp(page, 1, Math.Max(1, totalPages));

    /// <summary>
    /// The contiguous run of page numbers to render, centred on <paramref name="currentPage"/> and
    /// slid inwards so it never runs off either end. Callers detect a gap by comparing the first and
    /// last entries against 1 and <paramref name="totalPages"/>.
    /// </summary>
    public static IReadOnlyList<int> Build(int currentPage, int totalPages, int maxButtons = MaxButtons)
    {
        if (totalPages <= 0)
            return [];

        var size = Math.Min(totalPages, Math.Max(1, maxButtons));
        var current = Clamp(currentPage, totalPages);
        var start = current - (size - 1) / 2;

        // Slide rather than truncate, so the strip keeps a constant width near either end instead
        // of shrinking to three buttons on page 1.
        start = Math.Clamp(start, 1, totalPages - size + 1);

        return [.. Enumerable.Range(start, size)];
    }
}
