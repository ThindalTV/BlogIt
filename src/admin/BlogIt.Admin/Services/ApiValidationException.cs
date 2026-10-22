using System.Net;

namespace BlogIt.Admin.Services;

/// <summary>
/// A request the server refused as a ValidationProblem. <see cref="Exception.Message"/> is still the
/// first field's message, so a page that only shows <c>ex.Message</c> behaves as before; a form that
/// wants to mark each bad field reads <see cref="FieldErrors"/>.
/// </summary>
public sealed class ApiValidationException(
    string message,
    IReadOnlyDictionary<string, string> fieldErrors,
    HttpStatusCode statusCode)
    : HttpRequestException(message, null, statusCode)
{
    /// <summary>One message per invalid field, keyed case-insensitively by the server's field name.</summary>
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors;
}
