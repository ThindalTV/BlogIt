using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;

namespace BlogIt;

internal sealed partial class BlogItAdminAssets : IDisposable
{
    internal const string DirectoryName = "BlogItAdminAssets";

    private readonly PhysicalFileProvider _fileProvider;
    private readonly string _indexTemplate;

    public BlogItAdminAssets()
    {
        var rootPath = Path.Combine(AppContext.BaseDirectory, DirectoryName);
        var indexPath = Path.Combine(rootPath, "index.html");
        if (!File.Exists(indexPath))
        {
            throw new InvalidOperationException(
                $"BlogIt admin assets were not found at '{rootPath}'. " +
                "Build or publish the host with the BlogIt package/project reference so its build-transitive assets are copied.");
        }

        _fileProvider = new PhysicalFileProvider(rootPath);
        _indexTemplate = File.ReadAllText(indexPath);
        if (!BaseElementRegex().IsMatch(_indexTemplate))
        {
            throw new InvalidOperationException(
                $"The BlogIt admin shell at '{indexPath}' does not contain a base element.");
        }
    }

    public IFileProvider FileProvider => _fileProvider;

    public async Task WriteShellAsync(
        HttpContext context,
        string adminPath,
        CancellationToken cancellationToken = default)
    {
        var pathBase = context.Request.PathBase.Value ?? string.Empty;
        var baseHref = $"{pathBase}{(adminPath == "/" ? string.Empty : adminPath)}/";
        var encodedBaseHref = WebUtility.HtmlEncode(baseHref);
        var html = BaseElementRegex().Replace(
            _indexTemplate,
            _ => $"<base href=\"{encodedBaseHref}\" />",
            1);
        var body = Encoding.UTF8.GetBytes(html);

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = body.Length;
        context.Response.Headers.CacheControl = "no-cache, no-store";

        if (!HttpMethods.IsHead(context.Request.Method))
        {
            await context.Response.Body.WriteAsync(body, cancellationToken);
        }
    }

    public void Dispose() => _fileProvider.Dispose();

    [GeneratedRegex("<base\\s+href=(?:\"[^\"]*\"|'[^']*')\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BaseElementRegex();
}
