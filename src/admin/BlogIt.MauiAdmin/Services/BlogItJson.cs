using System.Text.Json;

namespace BlogIt.MauiAdmin.Services;

/// <summary>Shared JSON options matching the server's ASP.NET Core Web conventions
/// (camelCase on the wire, case-insensitive on read) so our PascalCase C# DTOs from
/// BlogIt.Contracts round-trip correctly.</summary>
public static class BlogItJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
