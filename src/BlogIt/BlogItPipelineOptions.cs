using Microsoft.AspNetCore.Builder;

namespace BlogIt;

/// <summary>
/// Controls which shared middleware <see cref="BlogItApplicationExtensions.UseBlogIt(IApplicationBuilder, Action{BlogItPipelineOptions})"/>
/// adds to the host's pipeline. Every flag defaults to <see langword="true"/>, which is right for an
/// application whose only authenticated area is the blog; turn one off when the host already owns
/// that middleware.
/// </summary>
/// <remarks>
/// <para>
/// BlogIt cannot simply always add these: authentication, authorization and rate limiting are
/// pipeline-wide, so adding a second copy makes them run twice per request — two permits charged
/// against one rate limit, and the host's principal recomputed. Nor can it always skip them: an
/// <see cref="IApplicationBuilder"/> built without <c>WebApplication</c>'s implicit auth middleware
/// would then have BlogIt endpoints whose authorization metadata is never evaluated, which ASP.NET
/// Core turns into a startup exception at the first request.
/// </para>
/// <para>
/// So the default is "add, unless the host demonstrably already did". Authentication and
/// authorization are detected automatically — every <c>UseAuthentication</c>/<c>UseAuthorization</c>
/// call marks the pipeline, and BlogIt reads those marks. Rate limiting leaves no such mark, and
/// there is nothing in <see cref="IApplicationBuilder"/> to infer it from, which is what these flags
/// are for. Ordering is also why they exist: a host that calls <c>UseAuthentication</c>
/// <em>after</em> <c>UseBlogIt</c> cannot be detected either, since nothing has happened yet at the
/// point BlogIt looks.
/// </para>
/// </remarks>
public sealed class BlogItPipelineOptions
{
    internal const string PropertyKey = "__BlogIt_PipelineOptions";

    /// <summary>
    /// The key <c>UseAuthentication</c> writes into <see cref="IApplicationBuilder.Properties"/>.
    /// Not a public ASP.NET Core constant, so it is duplicated here rather than referenced.
    /// </summary>
    internal const string AuthenticationMiddlewareSetKey = "__AuthenticationMiddlewareSet";

    /// <summary>The key <c>UseAuthorization</c> writes into <see cref="IApplicationBuilder.Properties"/>.</summary>
    internal const string AuthorizationMiddlewareSetKey = "__AuthorizationMiddlewareSet";

    /// <summary>
    /// Whether <c>UseBlogIt</c> adds <c>UseAuthentication</c>. Set to <see langword="false"/> when
    /// the host adds it itself after <c>UseBlogIt</c>; a call made before <c>UseBlogIt</c> is
    /// detected and needs no flag.
    /// </summary>
    public bool AddAuthenticationMiddleware { get; set; } = true;

    /// <summary>
    /// Whether <c>UseBlogIt</c> adds <c>UseAuthorization</c>. Set to <see langword="false"/> when
    /// the host adds it itself after <c>UseBlogIt</c>; a call made before <c>UseBlogIt</c> is
    /// detected and needs no flag.
    /// </summary>
    public bool AddAuthorizationMiddleware { get; set; } = true;

    /// <summary>
    /// Whether <c>UseBlogIt</c> adds <c>UseRateLimiter</c>. Set to <see langword="false"/> when the
    /// host calls <c>UseRateLimiter</c> itself, wherever in the pipeline: BlogIt's per-endpoint
    /// policies are honoured by whichever rate limiter middleware runs, so one call covers both, and
    /// two calls charge two permits for one request.
    /// </summary>
    public bool AddRateLimiterMiddleware { get; set; } = true;

    /// <summary>
    /// The options <c>UseBlogIt</c> recorded for <paramref name="application"/>, or the defaults when
    /// the parameterless overload was used.
    /// </summary>
    internal static BlogItPipelineOptions From(IApplicationBuilder application) =>
        application.Properties.TryGetValue(PropertyKey, out var stored)
            && stored is BlogItPipelineOptions options
            ? options
            : new BlogItPipelineOptions();

    /// <summary>
    /// Whether BlogIt should add <c>UseAuthentication</c> to <paramref name="application"/>: only
    /// when it is wanted and the host has not already added it.
    /// </summary>
    internal bool ShouldAddAuthentication(IApplicationBuilder application) =>
        AddAuthenticationMiddleware
        && !application.Properties.ContainsKey(AuthenticationMiddlewareSetKey);

    /// <summary>
    /// Whether BlogIt should add <c>UseAuthorization</c> to <paramref name="application"/>: only
    /// when it is wanted and the host has not already added it.
    /// </summary>
    internal bool ShouldAddAuthorization(IApplicationBuilder application) =>
        AddAuthorizationMiddleware
        && !application.Properties.ContainsKey(AuthorizationMiddlewareSetKey);
}
