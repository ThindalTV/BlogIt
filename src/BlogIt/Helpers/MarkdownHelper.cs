using Markdig;

namespace BlogIt.Shared.Helpers;

public static class MarkdownHelper
{
    // INTENTIONAL: raw HTML passthrough is left enabled, and the resulting HTML is not
    // sanitized before being rendered (see the MarkupString usages in BlogPostPage.razor,
    // CustomPage.razor, and the feed output in FeedService.cs). This means any BlogIt user —
    // not just visitors — can embed <script>/<iframe>/etc. in a post or page. That's accepted
    // by design: BlogIt's trust model treats every authenticated user as fully trusted content
    // author (equivalent to WordPress's "unfiltered_html" capability for admins), not a
    // sandboxed contributor. Do not grant an author account to anyone you wouldn't trust with
    // full site control. See AUDIT_REPORT.md findings #0/#1/#6/#20 for the full reasoning.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        .Build();

    /// <summary>Renders markdown (with HTML passthrough) to an HTML string.</summary>
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, Pipeline);
    }

    /// <summary>Strips all markdown/HTML and returns plain text (for SEO descriptions etc.).</summary>
    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        var html = ToHtml(markdown);
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ")
            .Replace("&nbsp;", " ")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Trim();
    }
}
