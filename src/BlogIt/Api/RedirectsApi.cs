using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Services;

namespace BlogIt.Api;

public static class RedirectsApi
{
    public static IEndpointRouteBuilder MapRedirectsApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/redirects")
            .WithTags("Redirects")
            .RequireAuthorization(BlogItDefaults.AdminAuthorizationPolicy);

        group.MapGet("/", async (IUrlRedirectService redirects) =>
            Results.Ok(await redirects.GetAllAsync()));
        group.MapPost("/", CreateRedirect);
        group.MapPut("/{id:guid}", UpdateRedirect);
        group.MapDelete("/{id:guid}", async (Guid id, IUrlRedirectService redirects) =>
            await redirects.DeleteAsync(id) ? Results.NoContent() : Results.NotFound());
        return app;
    }

    private static async Task<IResult> CreateRedirect(
        SaveUrlRedirectRequest request,
        BlogItOptions options,
        IUrlRedirectService redirects)
    {
        if (!RedirectPathValidator.TryNormalize(
                request.SourcePath,
                request.TargetUrl,
                options,
                out var sourcePath,
                out var targetUrl,
                out var error))
        {
            return Results.BadRequest(error);
        }

        var created = await redirects.CreateAsync(sourcePath, targetUrl, request.IsPermanent);
        return created is null
            ? Results.Conflict("A redirect already exists for this source path.")
            : Results.Created(
                BlogItPath.Combine(options.ApiPath, $"redirects/{created.Id}"),
                created);
    }

    private static async Task<IResult> UpdateRedirect(
        Guid id,
        SaveUrlRedirectRequest request,
        BlogItOptions options,
        IUrlRedirectService redirects)
    {
        if (!RedirectPathValidator.TryNormalize(
                request.SourcePath,
                request.TargetUrl,
                options,
                out var sourcePath,
                out var targetUrl,
                out var error))
        {
            return Results.BadRequest(error);
        }

        var updated = await redirects.UpdateAsync(id, sourcePath, targetUrl, request.IsPermanent);
        return updated is null
            ? Results.Conflict("The redirect does not exist or its source path is already in use.")
            : Results.Ok(updated);
    }
}

internal static class RedirectPathValidator
{
    private static readonly string[] FrameworkReservedPrefixes =
    [
        "/_framework",
        "/health",
        "/alive"
    ];

    private static readonly HashSet<string> ReservedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/",
        "/sitemap.xml",
        "/robots.txt",
        "/rss.xml",
        "/atom.xml"
    };

    public static bool TryNormalize(
        string source,
        string target,
        BlogItOptions options,
        out string sourcePath,
        out string targetUrl,
        out string error)
    {
        sourcePath = NormalizePath(source);
        targetUrl = target.Trim();
        error = string.Empty;

        // Cap tied to RedirectLimits.SourcePathLength, which is set by SQL Server's index key limit
        // rather than by anything about URLs — see that constant before changing this.
        if (sourcePath.Length == 0
            || sourcePath.Length > RedirectLimits.SourcePathLength
            || sourcePath.StartsWith("//", StringComparison.Ordinal)
            || sourcePath.Any(char.IsWhiteSpace)
            || sourcePath.Contains('?')
            || sourcePath.Contains('#')
            || sourcePath.Contains('\\')
            || IsReserved(sourcePath, options))
        {
            error = "The source must be a non-reserved local path without a query string or fragment.";
            return false;
        }

        if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var absoluteTarget))
        {
            if (absoluteTarget.Scheme is not ("http" or "https"))
            {
                error = "External targets must use HTTP or HTTPS.";
                return false;
            }
        }
        else
        {
            targetUrl = NormalizePath(targetUrl);
            if (targetUrl.Length == 0
                || targetUrl.StartsWith("//", StringComparison.Ordinal)
                || targetUrl.Contains('\\'))
            {
                error = "Internal targets must be valid local paths.";
                return false;
            }
        }

        if (targetUrl.Length > RedirectLimits.TargetUrlLength)
        {
            error = "The target URL cannot exceed 2,000 characters.";
            return false;
        }

        if (sourcePath.Equals(targetUrl, StringComparison.OrdinalIgnoreCase))
        {
            error = "A redirect cannot target itself.";
            return false;
        }

        return true;
    }

    private static bool IsReserved(string sourcePath, BlogItOptions options) =>
        ReservedPaths.Contains(sourcePath)
        || FrameworkReservedPrefixes
            .Append(options.ApiPath)
            .Append(options.AdminPath)
            .Append(options.MediaPath)
            .Any(prefix =>
            sourcePath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || sourcePath.StartsWith($"{prefix}/", StringComparison.OrdinalIgnoreCase));

    private static string NormalizePath(string value)
    {
        var path = value.Trim();
        if (path.Length == 0)
            return string.Empty;
        if (!path.StartsWith('/'))
            path = $"/{path}";
        return path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
