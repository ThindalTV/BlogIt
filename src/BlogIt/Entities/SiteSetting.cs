namespace BlogIt.Shared.Entities;

public class SiteSetting
{
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The stored value, or <see langword="null"/> when the setting exists but is not set — no
    /// analytics measurement ID, no AI key.
    /// </summary>
    /// <remarks>
    /// Nullable deliberately, and for the same reason optional post and page text is: null means the
    /// value does not exist, an empty string means it exists and is empty. This column used to be
    /// <c>NOT NULL</c>, which made "not set" unrepresentable — so first-run setup threw an unhandled
    /// <c>DbUpdateException</c> the moment a caller omitted an optional field, and the only reason the
    /// wizard never hit it was that it binds every input to <c>""</c> and so always sends something.
    /// </remarks>
    public string? Value { get; set; }
}
