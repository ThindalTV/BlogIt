using BlogIt.Services;

namespace BlogIt.Middleware;

public sealed class UrlRedirectMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IUrlRedirectService redirects,
        BlogItOptions options)
    {
        if ((HttpMethods.IsGet(context.Request.Method)
                || HttpMethods.IsHead(context.Request.Method))
            // Checked before the lookup, so a host that has confined blog redirects to its own
            // prefixes pays nothing for the feature on the rest of its URL space.
            && RedirectSourcePolicy.IsWithinConfiguredPrefixes(
                context.Request.Path.Value ?? "/",
                options))
        {
            var redirect = await redirects.FindAsync(context.Request.Path);
            if (redirect is not null)
            {
                context.Response.Redirect(
                    redirect.TargetUrl,
                    permanent: redirect.IsPermanent,
                    preserveMethod: false);
                return;
            }
        }

        await next(context);
    }
}

public static class UrlRedirectMiddlewareExtensions
{
    public static IApplicationBuilder UseUrlRedirects(this IApplicationBuilder app) =>
        app.UseMiddleware<UrlRedirectMiddleware>();
}
