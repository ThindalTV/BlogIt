using BlogIt.Web.Services;

namespace BlogIt.Web.Middleware;

public sealed class UrlRedirectMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IUrlRedirectService redirects)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            || HttpMethods.IsHead(context.Request.Method))
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
