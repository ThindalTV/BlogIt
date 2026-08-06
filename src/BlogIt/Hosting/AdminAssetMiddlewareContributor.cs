using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace BlogIt;

internal sealed partial class AdminAssetMiddlewareContributor(
    BlogItOptions options,
    BlogItAdminAssets assets)
    : IBlogItMiddlewareContributor
{
    public void Configure(IApplicationBuilder application)
    {
        var adminPath = new PathString(options.AdminPath);
        var adminPathWithSlash = options.AdminPath == "/" ? "/" : $"{options.AdminPath}/";
        var indexPath = new PathString(BlogItPath.Combine(options.AdminPath, "index.html"));

        application.Use(async (context, next) =>
        {
            if (options.AdminPath != "/" && context.Request.Path == adminPath)
            {
                context.Response.Redirect(
                    $"{context.Request.PathBase}{adminPathWithSlash}{context.Request.QueryString}");
                return;
            }

            if ((context.Request.Path == adminPathWithSlash || context.Request.Path == indexPath)
                && (HttpMethods.IsGet(context.Request.Method)
                    || HttpMethods.IsHead(context.Request.Method)))
            {
                await assets.WriteShellAsync(
                    context,
                    options.AdminPath,
                    context.RequestAborted);
                return;
            }

            await next();
        });

        var contentTypes = new FileExtensionContentTypeProvider();
        contentTypes.Mappings[".wasm"] = "application/wasm";
        contentTypes.Mappings[".webcil"] = "application/octet-stream";
        contentTypes.Mappings[".dat"] = "application/octet-stream";
        contentTypes.Mappings[".blat"] = "application/octet-stream";

        application.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = assets.FileProvider,
            RequestPath = options.AdminPath == "/" ? PathString.Empty : adminPath,
            ContentTypeProvider = contentTypes,
            OnPrepareResponse = context =>
            {
                context.Context.Response.Headers.XContentTypeOptions = "nosniff";
                var fileName = Path.GetFileName(context.File.Name);
                context.Context.Response.Headers.CacheControl =
                    FingerprintedFileRegex().IsMatch(fileName)
                        ? "public,max-age=31536000,immutable"
                        : "no-cache";
            }
        });
    }

    [GeneratedRegex("[._-][a-z0-9]{10}[._-]", RegexOptions.IgnoreCase)]
    private static partial Regex FingerprintedFileRegex();
}
