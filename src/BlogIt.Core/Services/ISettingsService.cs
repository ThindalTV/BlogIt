namespace BlogIt.Services;

/// <summary>
/// Reads and writes BlogIt's site settings.
/// </summary>
/// <remarks>
/// Values are nullable throughout: <see langword="null"/> means the setting is not set — no AI key,
/// no analytics measurement ID — and an empty string means it is set to nothing. A reader that only
/// wants to know "is this configured?" should treat both as unconfigured, which is what
/// <c>string.IsNullOrWhiteSpace</c> does and what every consumer here already did.
/// </remarks>
public interface ISettingsService
{
    /// <summary>The value for <paramref name="key"/>, or null if it is unset or absent.</summary>
    Task<string?> GetAsync(string key);

    /// <summary>Every stored setting. Values may be null.</summary>
    Task<Dictionary<string, string?>> GetAllAsync();

    /// <summary>Writes one setting. A null <paramref name="value"/> records it as not set.</summary>
    Task SetAsync(string key, string? value);

    /// <summary>Writes many settings. Null values record those settings as not set.</summary>
    Task SetManyAsync(Dictionary<string, string?> settings);
}
