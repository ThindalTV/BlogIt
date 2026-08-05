namespace BlogIt;

internal static class BlogItPath
{
    public static string Combine(string pathBase, string path)
    {
        var suffix = path.TrimStart('/');
        return pathBase == "/" ? $"/{suffix}" : $"{pathBase}/{suffix}";
    }

    public static string MediaPublicPath(BlogItOptions options, string storageKey) =>
        Combine(options.MediaPath, string.Join(
            '/',
            storageKey.Split('/').Select(Uri.EscapeDataString)));
}
