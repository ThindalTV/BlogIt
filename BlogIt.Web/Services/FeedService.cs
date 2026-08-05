using BlogIt.Shared;
using BlogIt.Shared.Data;
using BlogIt.Shared.Helpers;
using BlogIt.Web.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text;
using System.Xml;

namespace BlogIt.Web.Services;

public static class FeedService
{
    public const int MaxItems = 50;

    private const string AtomNamespace = "http://www.w3.org/2005/Atom";
    private const string ContentNamespace = "http://purl.org/rss/1.0/modules/content/";
    private const string DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";

    public static async Task<string> CreateRssAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration configuration,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var feed = await LoadFeedAsync(db, settings, configuration, request, cancellationToken);
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
            writer.WriteAttributeString("href", feed.RssUrl);
            writer.WriteAttributeString("rel", "self");
            writer.WriteAttributeString("type", FeedsApi.RssContentType.Split(';')[0]);
            writer.WriteEndElement();

            if (feed.Items.Count > 0)
                writer.WriteElementString("lastBuildDate", ToRfc822(feed.UpdatedAt));

            foreach (var item in feed.Items)
            {
                writer.WriteStartElement("item");
                writer.WriteElementString("title", item.Title);
                writer.WriteElementString("link", item.Url);
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
                    "dc", "date", DublinCoreNamespace, ToUtc(item.UpdatedAt).ToString("O"));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        });
    }

    public static async Task<string> CreateAtomAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration configuration,
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        var feed = await LoadFeedAsync(db, settings, configuration, request, cancellationToken);
        return WriteXml(writer =>
        {
            writer.WriteStartElement("feed", AtomNamespace);
            writer.WriteElementString("title", AtomNamespace, feed.Title);
            writer.WriteElementString("subtitle", AtomNamespace, feed.Description);
            WriteAtomLink(writer, feed.SiteUrl, "alternate", "text/html");
            WriteAtomLink(writer, feed.AtomUrl, "self", FeedsApi.AtomContentType.Split(';')[0]);
            writer.WriteElementString("id", AtomNamespace, feed.SiteUrl);
            writer.WriteElementString("updated", AtomNamespace, ToUtc(feed.UpdatedAt).ToString("O"));

            foreach (var item in feed.Items)
            {
                writer.WriteStartElement("entry", AtomNamespace);
                writer.WriteElementString("title", AtomNamespace, item.Title);
                WriteAtomLink(writer, item.Url, "alternate", "text/html");
                writer.WriteElementString("id", AtomNamespace, item.StableId);
                writer.WriteElementString(
                    "published", AtomNamespace, ToUtc(item.PublishedAt).ToString("O"));
                writer.WriteElementString(
                    "updated", AtomNamespace, ToUtc(item.UpdatedAt).ToString("O"));
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

    private static async Task<FeedDocument> LoadFeedAsync(
        BlogItDbContext db,
        ISettingsService settings,
        IConfiguration configuration,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var siteUrl = ResolveSiteUrl(
            await settings.GetAsync(SettingKeys.SiteUrl),
            configuration[SettingKeys.SiteUrl],
            request);
        var siteName = await settings.GetAsync(SettingKeys.SiteName);
        var siteDescription = await settings.GetAsync(SettingKeys.SiteDescription);

        var posts = await db.BlogPosts
            .Where(post => post.IsPublished && post.PublishedAt != null)
            .OrderByDescending(post => post.PublishedAt)
            .ThenByDescending(post => post.Id)
            .Select(post => new
            {
                post.Id,
                post.Title,
                post.Slug,
                post.Summary,
                post.Content,
                PublishedAt = post.PublishedAt!.Value,
                post.CreatedAt,
                post.UpdatedAt,
                Author = post.Author == null ? null : post.Author.DisplayName
            })
            .AsNoTracking()
            .Take(MaxItems)
            .ToListAsync(cancellationToken);

        var items = posts.Select(post =>
        {
            var publishedAt = ToUtc(post.PublishedAt);
            var updatedAt = ToUtc(post.UpdatedAt);
            if (updatedAt < publishedAt)
                updatedAt = publishedAt;

            return new FeedItem(
                post.Title,
                new Uri(
                    new Uri(siteUrl),
                    BlogUrlHelper.GetPostPath(post.Slug, post.PublishedAt, post.CreatedAt)).AbsoluteUri,
                $"urn:uuid:{post.Id:D}",
                MarkdownHelper.ToHtml(post.Summary),
                MarkdownHelper.ToHtml(post.Content ?? post.Summary),
                post.Author,
                publishedAt,
                updatedAt);
        }).ToList();

        return new FeedDocument(
            string.IsNullOrWhiteSpace(siteName) ? "BlogIt" : siteName,
            string.IsNullOrWhiteSpace(siteDescription)
                ? $"Latest posts from {(string.IsNullOrWhiteSpace(siteName) ? "BlogIt" : siteName)}"
                : siteDescription,
            siteUrl,
            new Uri(new Uri(siteUrl), "/rss.xml").AbsoluteUri,
            new Uri(new Uri(siteUrl), "/atom.xml").AbsoluteUri,
            items.Count == 0 ? DateTime.UnixEpoch : items.Max(item => item.UpdatedAt),
            items);
    }

    private static string ResolveSiteUrl(
        string? siteSettingUrl,
        string? configurationUrl,
        HttpRequest request)
    {
        foreach (var candidate in new[] { siteSettingUrl, configurationUrl })
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var configuredUri) &&
                (configuredUri.Scheme == Uri.UriSchemeHttp || configuredUri.Scheme == Uri.UriSchemeHttps))
            {
                return configuredUri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
            }
        }

        var origin = $"{request.Scheme}://{request.Host}{request.PathBase}/";
        if (Uri.TryCreate(origin, UriKind.Absolute, out var requestUri) &&
            (requestUri.Scheme == Uri.UriSchemeHttp || requestUri.Scheme == Uri.UriSchemeHttps))
        {
            return requestUri.AbsoluteUri;
        }

        throw new InvalidOperationException("A valid HTTP(S) site URL or request origin is required.");
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

    private static string ToRfc822(DateTime value) =>
        ToUtc(value).ToString("r", CultureInfo.InvariantCulture);

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }

    private sealed record FeedDocument(
        string Title,
        string Description,
        string SiteUrl,
        string RssUrl,
        string AtomUrl,
        DateTime UpdatedAt,
        IReadOnlyList<FeedItem> Items);

    private sealed record FeedItem(
        string Title,
        string Url,
        string StableId,
        string SummaryHtml,
        string ContentHtml,
        string? Author,
        DateTime PublishedAt,
        DateTime UpdatedAt);
}
