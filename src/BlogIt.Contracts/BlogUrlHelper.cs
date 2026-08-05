namespace BlogIt.Shared.Helpers;

public static class BlogUrlHelper
{
    public static int GetYear(DateTime? publishedAt, DateTime createdAt) =>
        (publishedAt ?? createdAt).Year;

    public static string GetPostPath(string slug, DateTime? publishedAt, DateTime createdAt) =>
        $"/{GetYear(publishedAt, createdAt)}/{slug}";
}
