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

    /// <summary>Counts the words in a markdown body.</summary>
    /// <remarks>
    /// Renders to plain text first so that markup, link targets and HTML attributes are not counted,
    /// and so that block tags become the whitespace that keeps adjacent words apart. What remains is
    /// split on whitespace — a deliberately plain definition, which means fenced code and bare URLs
    /// count as words. Stability matters more than precision here: the figure is stored on the post
    /// and only recomputed when the body changes, so a rule that is easy to predict beats one that
    /// is marginally more accurate.
    /// </remarks>
    public static int CountWords(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return 0;

        return ToPlainText(markdown)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
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
