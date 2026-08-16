namespace BlogIt.Admin.Services;

/// <summary>
/// Builds and validates the <c>returnUrl</c> that carries a user back to the page an expired or
/// revoked session interrupted. Kept out of the components so both ends of the round trip — the
/// handler that writes the value and the login page that consumes it — share one set of rules, and
/// so those rules are unit testable without rendering anything.
/// </summary>
/// <remarks>
/// Public rather than internal only so the tests can reach it: InternalsVisibleTo on this assembly
/// would also expose the top-level <c>Program</c> that Blazor WASM generates, which then collides
/// with the sample host's <c>Program</c> inside the test assembly (CS0433).
/// </remarks>
public static class AdminLoginRedirect
{
    /// <summary>Base-relative route of the login page.</summary>
    public const string LoginPath = "login";

    /// <summary>Base-relative route of the dashboard, and the fallback after re-authenticating.</summary>
    public const string DashboardPath = "";

    /// <summary>Query-string key holding the interrupted page.</summary>
    public const string ReturnUrlKey = "returnUrl";

    /// <summary>
    /// Login URL to send a rejected request's user to.
    /// </summary>
    /// <param name="currentRelativePath">
    /// Base-relative path of the page they were on, i.e. <c>NavigationManager.ToBaseRelativePath</c>
    /// of the current URI.
    /// </param>
    /// <returns>
    /// <c>login</c>, with a <c>returnUrl</c> when there is somewhere worth returning to. The login
    /// and setup screens are excluded: bouncing either onto itself with a returnUrl pointing back at
    /// itself is at best noise and at worst a loop.
    /// </returns>
    public static string BuildLoginUrl(string? currentRelativePath)
    {
        var path = currentRelativePath?.Trim() ?? "";
        if (path.Length == 0 || IsAnonymousScreen(path))
            return LoginPath;

        return $"{LoginPath}?{ReturnUrlKey}={Uri.EscapeDataString(path)}";
    }

    /// <summary>
    /// Turns a <c>returnUrl</c> query value back into a path to navigate to after a successful
    /// login, falling back to the dashboard for anything that is not a plain base-relative path.
    /// The value reaches us through the address bar, so an absolute URL, a protocol-relative
    /// <c>//host</c>, a rooted <c>/path</c> that would escape the admin's base href, or a
    /// <c>javascript:</c> payload must never be navigated to.
    /// </summary>
    public static string ResolveReturnPath(string? returnUrl)
    {
        var candidate = returnUrl?.Trim() ?? "";
        if (candidate.Length == 0)
            return DashboardPath;

        // Rooted and protocol-relative forms are rejected before the Uri check because
        // Uri.TryCreate with UriKind.Relative happily accepts both.
        if (candidate.StartsWith('/') || candidate.StartsWith('\\'))
            return DashboardPath;

        if (!Uri.TryCreate(candidate, UriKind.Relative, out _))
            return DashboardPath;

        // "javascript:alert(1)" parses as a relative URI in .NET, so scheme-like prefixes are
        // filtered by hand. A colon has no legitimate place in an admin route.
        if (candidate.Contains(':'))
            return DashboardPath;

        return candidate;
    }

    private static bool IsAnonymousScreen(string relativePath)
    {
        var route = relativePath.Split('?', '#')[0].Trim('/');
        return route.Equals(LoginPath, StringComparison.OrdinalIgnoreCase)
            || route.Equals("setup", StringComparison.OrdinalIgnoreCase);
    }
}
