namespace BlogIt.Shared.Helpers;

public static class UrlValidator
{
    /// <summary>True if <paramref name="value"/> is a non-empty, absolute http(s) URL.</summary>
    public static bool IsValidAbsoluteHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
