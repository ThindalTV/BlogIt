using System.Net;
using System.Text.Json;
using BlogIt.MauiAdmin.Models;

namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// The one place every non-2xx API response body gets interpreted, per the server's
/// actual (inconsistent) wire shapes: an empty body, a bare JSON string literal (e.g.
/// "Current password is incorrect."), or an RFC7807 ValidationProblemDetails object
/// (schedule/slug conflicts). Centralizing this fixes a systemic bug present in both
/// the previous MAUI client and the reference Blazor admin, where real server
/// validation messages were silently discarded in favor of a generic status-code text.
/// </summary>
public static class ApiResponseParser
{
    public static async Task<ApiError> ParseErrorAsync(HttpResponseMessage response, CancellationToken ct = default)
    {
        var statusCode = response.StatusCode;
        var isAuthExpired = statusCode == HttpStatusCode.Unauthorized;

        string? body = null;
        try
        {
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch
        {
            // No readable body — fall through to a generic message.
        }

        if (string.IsNullOrWhiteSpace(body))
            return new ApiError(statusCode, DefaultMessage(statusCode), IsAuthExpired: isAuthExpired);

        var trimmed = body.TrimStart();

        if (trimmed.StartsWith('{'))
        {
            var problem = TryParseValidationProblem(body, statusCode, isAuthExpired);
            if (problem is not null) return problem;
        }
        else if (trimmed.StartsWith('"'))
        {
            var literal = TryParseBareStringLiteral(body, statusCode, isAuthExpired);
            if (literal is not null) return literal;
        }

        return new ApiError(statusCode, DefaultMessage(statusCode), IsAuthExpired: isAuthExpired);
    }

    private static ApiError? TryParseValidationProblem(string body, HttpStatusCode statusCode, bool isAuthExpired)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (!root.TryGetProperty("errors", out var errorsElement) || errorsElement.ValueKind != JsonValueKind.Object)
                return null;

            var errors = errorsElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToArray());

            var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var firstFieldMessage = errors.Values.SelectMany(v => v).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));
            var message = firstFieldMessage ?? title ?? DefaultMessage(statusCode);

            return new ApiError(statusCode, message, errors, isAuthExpired);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ApiError? TryParseBareStringLiteral(string body, HttpStatusCode statusCode, bool isAuthExpired)
    {
        try
        {
            var literal = JsonSerializer.Deserialize<string>(body);
            return string.IsNullOrWhiteSpace(literal)
                ? null
                : new ApiError(statusCode, literal, IsAuthExpired: isAuthExpired);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string DefaultMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
        HttpStatusCode.NotFound => "The requested item was not found.",
        HttpStatusCode.Conflict => "This conflicts with existing data.",
        HttpStatusCode.BadRequest => "The request was invalid.",
        _ => $"Request failed ({(int)statusCode} {statusCode}).",
    };
}
