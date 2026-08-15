namespace BlogIt.Shared;

/// <summary>Bounds for URL redirect fields.</summary>
public static class RedirectLimits
{
    /// <summary>
    /// Maximum source path length. 450 characters is 900 bytes of <c>nvarchar</c>, which keeps the
    /// unique index on the column inside SQL Server's 1700-byte nonclustered key limit.
    /// </summary>
    /// <remarks>
    /// This number is load-bearing in two places at once — the column width and the validator that
    /// rejects an over-long path. It was previously 1000 in both, which is 2000 bytes: the index was
    /// created with a warning and then failed at insert time for any row that used the full length
    /// the validator permitted. Raising it again means raising the index limit problem with it.
    /// </remarks>
    public const int SourcePathLength = 450;

    /// <summary>Maximum target length. Not indexed, so only the column width constrains it.</summary>
    public const int TargetUrlLength = 2000;
}
