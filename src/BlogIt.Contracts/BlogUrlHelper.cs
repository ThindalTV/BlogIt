namespace BlogIt.Shared.Helpers;

public static class BlogUrlHelper
{
    /// <summary>
    /// The year a post's permalink is filed under: its publication year, or its creation year while
    /// it is still a draft.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="DateTimeOffset.UtcDateTime"/> rather than the offset's own calendar on
    /// purpose. The year is part of the permalink, so deriving it from a non-UTC offset would file a
    /// post published near midnight on 31 December under a different year than the one its URL was
    /// first issued with — silently breaking every existing link to it.
    /// </remarks>
    public static int GetYear(DateTimeOffset? publishedAt, DateTimeOffset createdAt) =>
        (publishedAt ?? createdAt).UtcDateTime.Year;

    public static string GetPostPath(string slug, DateTimeOffset? publishedAt, DateTimeOffset createdAt) =>
        $"/{GetYear(publishedAt, createdAt)}/{slug}";
}
