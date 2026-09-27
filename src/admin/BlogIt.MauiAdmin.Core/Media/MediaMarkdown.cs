namespace BlogIt.MauiAdmin.Core.Media;

/// <summary>
/// Builds the Markdown an editor inserts when the user picks something from the media library.
/// </summary>
public static class MediaMarkdown
{
    /// <summary>
    /// An image embed for images, a plain link for everything else. The path is used as given —
    /// server-relative — because that is how the rendered content resolves media against the
    /// public site root, and an absolute URL would pin the content to whichever host the admin
    /// happened to be connected to when they wrote it. That matters here: the same author edits
    /// several blogs from this app.
    /// </summary>
    public static string ForMedia(string title, string publicPath, string contentType) =>
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            ? $"![{title}]({publicPath})"
            : $"[{title}]({publicPath})";

    /// <summary>
    /// Inserts <paramref name="markdown"/> at <paramref name="cursorPosition"/>, clamping the
    /// position into the text. Editors report -1 when they have never had focus, and a stale
    /// position can outlive the text it referred to.
    /// </summary>
    public static string InsertAt(string content, string markdown, int cursorPosition)
    {
        content ??= string.Empty;
        var position = Math.Clamp(cursorPosition, 0, content.Length);
        return content.Insert(position, markdown);
    }
}
