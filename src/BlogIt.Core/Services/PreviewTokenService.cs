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

    /// <summary>
    /// Removes expired grants that were never looked up again (a redeemed or expired token is
    /// otherwise only cleaned up the next time something tries to use it — a preview link
    /// generated and never clicked would sit in memory for the process lifetime without this).
    /// Called periodically by <see cref="PublicationSchedulingService"/>'s existing timer.
    /// </summary>
    void SweepExpired();
}

public sealed class PreviewTokenService(TimeProvider timeProvider) : IPreviewTokenService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<Guid, PreviewGrant> tokens = new();

    /// <summary>Test-only visibility into the backing store's size, to verify SweepExpired.</summary>
    internal int GrantCount => tokens.Count;

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

    public void SweepExpired()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (token, grant) in tokens)
        {
            if (grant.ExpiresAt <= now)
                tokens.TryRemove(token, out _);
        }
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
