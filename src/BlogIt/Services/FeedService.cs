using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Helpers;
using BlogIt.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Xml;

namespace BlogIt.Services;

/// <summary>
/// Renders <see cref="BlogFeed"/> data as RSS 2.0 and Atom 1.0.
/// </summary>
/// <remarks>
/// The <c>Create…Async</c> overloads load and render in one step for the built-in endpoints. A
/// host that turned those endpoints off gets the same documents by taking
/// <see cref="ISiteMetadataService.GetFeedAsync"/> and passing the result to
/// <see cref="CreateRss(BlogFeed)"/> or <see cref="CreateAtom(BlogFeed)"/>.
/// </remarks>
public static class FeedService
{
    /// <summary>How many items the built-in feed endpoints include.</summary>
    public const int MaxItems = 50;

    private const string AtomNamespace = "http://www.w3.org/2005/Atom";
    private const string ContentNamespace = "http://purl.org/rss/1.0/modules/content/";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";

    public static async Task<string> CreateRssAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration configuration,
        HttpRequest request,
        CancellationToken cancellationToken = default) =>
        CreateRss(await LoadFeedAsync(db, settings, configuration, request, cancellationToken));

    public static async Task<string> CreateAtomAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration configuration,
        HttpRequest request,
        CancellationToken cancellationToken = default) =>
        CreateAtom(await LoadFeedAsync(db, settings, configuration, request, cancellationToken));

    /// <summary>Renders <paramref name="feed"/> as an RSS 2.0 document.</summary>
    public static string CreateRss(BlogFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        feed = Sanitize(feed);

        return WriteXml(writer =>
        {
            writer.WriteStartElement("rss");
            writer.WriteAttributeString("version", "2.0");
            writer.WriteAttributeString("xmlns", "atom", null, AtomNamespace);
            writer.WriteAttributeString("xmlns", "content", null, ContentNamespace);
            writer.WriteAttributeString("xmlns", "dc", null, DublinCoreNamespace);
            writer.WriteStartElement("channel");

            writer.WriteElementString("title", feed.Title);
            writer.WriteElementString("link", feed.SiteUrl);
            writer.WriteElementString("description", feed.Description);
            writer.WriteElementString("language", "en");
            writer.WriteStartElement("atom", "link", AtomNamespace);
            writer.WriteAttributeString("href", AbsoluteUrl(feed.SiteUrl, "/rss.xml"));
            writer.WriteAttributeString("rel", "self");
            writer.WriteAttributeString("type", FeedsApi.RssContentType.Split(';')[0]);
            writer.WriteEndElement();

            if (feed.Items.Count > 0)
                writer.WriteElementString("lastBuildDate", ToRfc822(feed.UpdatedAt));

            foreach (var item in feed.Items)
            {
                writer.WriteStartElement("item");
                writer.WriteElementString("title", item.Title);
                writer.WriteElementString("link", AbsoluteUrl(feed.SiteUrl, item.Path));
                writer.WriteStartElement("guid");
                writer.WriteAttributeString("isPermaLink", "false");
                writer.WriteString(item.StableId);
                writer.WriteEndElement();
                writer.WriteElementString("description", item.SummaryHtml);
                writer.WriteElementString("content", "encoded", ContentNamespace, item.ContentHtml);
                if (!string.IsNullOrWhiteSpace(item.Author))
                    writer.WriteElementString("dc", "creator", DublinCoreNamespace, item.Author);
                writer.WriteElementString("pubDate", ToRfc822(item.PublishedAt));
                writer.WriteElementString(
                    "dc", "date", DublinCoreNamespace, item.UpdatedAt.ToString("O"));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        });
    }

    /// <summary>Renders <paramref name="feed"/> as an Atom 1.0 document.</summary>
    public static string CreateAtom(BlogFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);

        feed = Sanitize(feed);

        return WriteXml(writer =>
        {
            writer.WriteStartElement("feed", AtomNamespace);
            writer.WriteElementString("title", AtomNamespace, feed.Title);
            writer.WriteElementString("subtitle", AtomNamespace, feed.Description);
            WriteAtomLink(writer, feed.SiteUrl, "alternate", "text/html");
            WriteAtomLink(
                writer,
                AbsoluteUrl(feed.SiteUrl, "/atom.xml"),
                "self",
                FeedsApi.AtomContentType.Split(';')[0]);
            writer.WriteElementString("id", AtomNamespace, feed.SiteUrl);
            writer.WriteElementString("updated", AtomNamespace, feed.UpdatedAt.ToString("O"));

            foreach (var item in feed.Items)
            {
                writer.WriteStartElement("entry", AtomNamespace);
                writer.WriteElementString("title", AtomNamespace, item.Title);
                WriteAtomLink(writer, AbsoluteUrl(feed.SiteUrl, item.Path), "alternate", "text/html");
                writer.WriteElementString("id", AtomNamespace, item.StableId);
                writer.WriteElementString(
                    "published", AtomNamespace, item.PublishedAt.ToString("O"));
                writer.WriteElementString(
                    "updated", AtomNamespace, item.UpdatedAt.ToString("O"));
                WriteTypedAtomElement(writer, "summary", item.SummaryHtml);
                WriteTypedAtomElement(writer, "content", item.ContentHtml);

                if (!string.IsNullOrWhiteSpace(item.Author))
                {
                    writer.WriteStartElement("author", AtomNamespace);
                    writer.WriteElementString("name", AtomNamespace, item.Author);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        });
    }

    private static async Task<BlogFeed> LoadFeedAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration configuration,
        HttpRequest request,
        CancellationToken cancellationToken) =>
        await SiteMetadataService.LoadFeedAsync(
            db,
            SiteUrlResolver.Resolve(
                await settings.GetAsync(SettingKeys.SiteUrl),
                configuration[SettingKeys.SiteUrl],
                request),
            await settings.GetAsync(SettingKeys.SiteName),
            await settings.GetAsync(SettingKeys.SiteDescription),
            MaxItems,
            cancellationToken);

    // Absolutizing lives here, in the renderer, and not on BlogFeedItem - keeping it out of the
    // public records is what let the fix below stay a change to one line of rendering rather than
    // a change to the published contract.
    //
    // Concatenation against the trimmed base, and never `new Uri(base, path)`: the Uri overload
    // resolves a rooted path against the site URL's *origin*, so a blog mounted at
    // https://example.com/blog/ used to emit https://example.com/rss.xml and item links with the
    // prefix silently dropped. Same rule, and same reason, as SiteMetadataService's sitemap entries
    // and SitemapApi's base handling. Paths passed here always start with '/' (BlogFeedItem.Path
    // guarantees it, and the two literals are rooted), so this cannot produce a doubled slash.
    private static string AbsoluteUrl(string siteUrl, string path) =>
        siteUrl.TrimEnd('/') + path;

    /// <summary>
    /// Returns <paramref name="feed"/> with every string field stripped of characters XML 1.0
    /// cannot represent, so one post cannot take the whole document down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="XmlWriterSettings.CheckCharacters"/> defaults to true, and rightly so: a raw
    /// control character in the output is a feed no reader can parse. But it threw from the middle
    /// of a half-written document, so a single vertical tab pasted into one post - Word and PDF
    /// copy-paste produce them routinely - answered <c>/rss.xml</c> and <c>/atom.xml</c> with a 500
    /// for every post on the site.
    /// </para>
    /// <para>
    /// Stripping rather than skipping the offending item. Skipping was considered and rejected: it
    /// silently drops a post from every subscriber's reader, and because feed readers key on the
    /// item id, a post that appears once the character is edited out arrives as new content weeks
    /// late. Stripping alters the content, but only by removing characters that are invisible in
    /// any reader and that XML has no representation for at all - there is no lossless option here.
    /// Doing it once over the whole record, rather than at each of the twenty write calls, is what
    /// makes it impossible for a later field to be added without the guard.
    /// </para>
    /// </remarks>
    private static BlogFeed Sanitize(BlogFeed feed) => feed with
    {
        Title = XmlText(feed.Title),
        Description = XmlText(feed.Description),
        SiteUrl = XmlText(feed.SiteUrl),
        Items = [.. feed.Items.Select(item => item with
        {
            Title = XmlText(item.Title),
            Path = XmlText(item.Path),
            StableId = XmlText(item.StableId),
            SummaryHtml = XmlText(item.SummaryHtml),
            ContentHtml = XmlText(item.ContentHtml),
            Author = XmlText(item.Author)
        })]
    };

    /// <summary>
    /// <paramref name="value"/> with everything outside the XML 1.0 <c>Char</c> production removed:
    /// the C0 controls other than tab, newline and carriage return, unpaired surrogates, and the
    /// two non-characters at the end of the BMP.
    /// </summary>
    /// <remarks>
    /// Returns the same instance when there is nothing to strip, which is every normal post - the
    /// scan is a read over the string with no allocation, so the common case pays only for that.
    /// </remarks>
    [return: NotNullIfNotNull(nameof(value))]
    private static string? XmlText(string? value)
    {
        if (value is null)
            return null;

        var firstBad = IndexOfInvalidChar(value, 0);
        if (firstBad < 0)
            return value;

        var builder = new StringBuilder(value.Length);
        var start = 0;
        while (firstBad >= 0)
        {
            builder.Append(value, start, firstBad - start);
            start = firstBad + 1;
            firstBad = IndexOfInvalidChar(value, start);
        }

        builder.Append(value, start, value.Length - start);
        return builder.ToString();
    }

    // A surrogate is legal only as half of a well-formed pair, so it is checked against its
    // neighbour; XmlConvert.IsXmlChar rejects every surrogate on its own.
    private static int IndexOfInvalidChar(string value, int startIndex)
    {
        for (var index = startIndex; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsHighSurrogate(current)
                && index + 1 < value.Length
                && XmlConvert.IsXmlSurrogatePair(value[index + 1], current))
            {
                index++;
                continue;
            }

            if (!XmlConvert.IsXmlChar(current))
                return index;
        }

        return -1;
    }

    private static void WriteAtomLink(XmlWriter writer, string href, string rel, string type)
    {
        writer.WriteStartElement("link", AtomNamespace);
        writer.WriteAttributeString("href", href);
        writer.WriteAttributeString("rel", rel);
        writer.WriteAttributeString("type", type);
        writer.WriteEndElement();
    }

    private static void WriteTypedAtomElement(XmlWriter writer, string name, string value)
    {
        writer.WriteStartElement(name, AtomNamespace);
        writer.WriteAttributeString("type", "html");
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    private static string WriteXml(Action<XmlWriter> write)
    {
        var builder = new StringBuilder();
        using var stringWriter = new Utf8StringWriter(builder);
        using (var writer = XmlWriter.Create(stringWriter, new XmlWriterSettings
        {
            Async = false,
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            NewLineHandling = NewLineHandling.Entitize
        }))
        {
            write(writer);
        }

        return builder.ToString();
    }

    // Feed timestamps arrive already normalized to UTC by the loader, so this only formats.
    private static string ToRfc822(DateTime value) =>
        value.ToString("r", CultureInfo.InvariantCulture);

    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
