using System.Net;

namespace BlogIt.MauiAdmin.Models;

/// <summary>
/// A parsed, user-presentable description of a failed API call. <see cref="Message"/> is
/// always populated by <see cref="Services.ApiResponseParser"/> from whatever shape the
/// server actually returned (empty body, a bare JSON string, or an RFC7807
/// ValidationProblemDetails object) — callers should show this directly rather than a
/// generic "request failed" string.
/// </summary>
public record ApiError(
    HttpStatusCode StatusCode,
    string Message,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    bool IsAuthExpired = false);

public class ApiResult
{
    public bool Success { get; init; }
    public ApiError? Error { get; init; }

    public static ApiResult Ok() => new() { Success = true };
    public static ApiResult Fail(ApiError error) => new() { Success = false, Error = error };
}

public class ApiResult<T> : ApiResult
{
    public T? Value { get; init; }

    public static ApiResult<T> Ok(T value) => new() { Success = true, Value = value };
    public static new ApiResult<T> Fail(ApiError error) => new() { Success = false, Error = error };
}
