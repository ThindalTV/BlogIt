using System.Collections.Concurrent;

namespace BlogIt.Services;

public enum PreviewContentType
{
    Post,
    Page
}

public interface IPreviewTokenService
{
    (Guid Token, DateTimeOffset ExpiresAt) Issue(PreviewContentType contentType, Guid contentId);
    bool TryAuthorize(
        HttpContext httpContext,
        Guid? token,
        PreviewContentType contentType,
        Guid contentId);
}

public sealed class PreviewTokenService(TimeProvider timeProvider) : IPreviewTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<Guid, PreviewGrant> tokens = new();

    public (Guid Token, DateTimeOffset ExpiresAt) Issue(
        PreviewContentType contentType,
        Guid contentId)
    {
        var expiresAt = timeProvider.GetUtcNow().Add(Lifetime);
        var token = Guid.NewGuid();
        tokens[token] = new PreviewGrant(contentType, contentId, expiresAt);
        return (token, expiresAt);
    }

    public bool TryAuthorize(
        HttpContext httpContext,
        Guid? token,
        PreviewContentType contentType,
        Guid contentId)
    {
        var cookieName = GetCookieName(contentType, contentId);
        if (httpContext.Items.ContainsKey(cookieName))
            return true;

        if (token.HasValue
            && TryRedeem(token.Value, contentType, contentId, out var expiresAt))
        {
            httpContext.Response.Cookies.Append(
                cookieName,
                token.Value.ToString("N"),
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = httpContext.Request.IsHttps,
                    SameSite = SameSiteMode.Strict,
                    Path = httpContext.Request.Path,
                    Expires = expiresAt
                });
            httpContext.Items[cookieName] = true;
            return true;
        }

        return httpContext.Request.Cookies.TryGetValue(cookieName, out var cookieValue)
            && Guid.TryParse(cookieValue, out var cookieToken)
            && ValidateAccess(cookieToken, contentType, contentId);
    }

    private bool TryRedeem(
        Guid token,
        PreviewContentType contentType,
        Guid contentId,
        out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (!TryGetValidGrant(token, contentType, contentId, out var grant))
            return false;

        if (Interlocked.CompareExchange(ref grant.Redeemed, 1, 0) != 0)
            return false;

        expiresAt = grant.ExpiresAt;
        return true;
    }

    private bool ValidateAccess(Guid token, PreviewContentType contentType, Guid contentId) =>
        TryGetValidGrant(token, contentType, contentId, out var grant)
        && Volatile.Read(ref grant.Redeemed) == 1;

    private bool TryGetValidGrant(
        Guid token,
        PreviewContentType contentType,
        Guid contentId,
        out PreviewGrant grant)
    {
        if (!tokens.TryGetValue(token, out grant!))
            return false;

        if (grant.ExpiresAt <= timeProvider.GetUtcNow())
        {
            tokens.TryRemove(token, out _);
            return false;
        }

        return grant.ContentType == contentType && grant.ContentId == contentId;
    }

    private static string GetCookieName(PreviewContentType contentType, Guid contentId) =>
        $"BlogItPreview_{contentType}_{contentId:N}";

    private sealed class PreviewGrant(
        PreviewContentType contentType,
        Guid contentId,
        DateTimeOffset expiresAt)
    {
        public PreviewContentType ContentType { get; } = contentType;
        public Guid ContentId { get; } = contentId;
        public DateTimeOffset ExpiresAt { get; } = expiresAt;
        public int Redeemed;
    }
}
