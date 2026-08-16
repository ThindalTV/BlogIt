using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Helpers;
using BlogIt.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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

    // Absolutizing lives here, in the renderer, and not on BlogFeedItem: this resolves a rooted
    // path against the site URL's *origin*, so it drops the prefix of a blog mounted under one -
    // a known open defect in the rendered feeds. Keeping it out of the public records means fixing
    // it stays a change to one line of rendering rather than a change to the published contract.
    private static string AbsoluteUrl(string siteUrl, string path) =>
        new Uri(new Uri(siteUrl), path).AbsoluteUri;

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
