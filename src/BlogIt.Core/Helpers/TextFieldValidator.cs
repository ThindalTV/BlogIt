namespace BlogIt.Shared.Helpers;

/// <summary>
/// Boundary checks for the text fields whose EF configuration declares them required, bounded, or
/// both.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="SeoMetadataValidator"/>, which covers the optional SEO columns;
/// these are the required ones. A bounded column refuses an over-long value and a required column
/// refuses null, and either way the refusal arrives as a <c>DbUpdateException</c> from
/// <c>SaveChanges</c> — a 500 that names nothing and, on a create, leaves the caller unable to tell
/// a typo from an outage. Checking here turns both into a 400 that names the field.
/// <para>
/// Every method takes the dictionary <c>Results.ValidationProblem</c> consumes so one endpoint can
/// collect several fields into a single response instead of returning on the first bad one.
/// </para>
/// </remarks>
public static class TextFieldValidator
{
    /// <summary>
    /// Requires a non-blank value no longer than <paramref name="maxLength"/>.
    /// </summary>
    /// <param name="field">
    /// The JSON member name, since that is the key a client looks the message up under.
    /// </param>
    /// <param name="label">Human-readable field name for the message text.</param>
    public static void CheckRequired(
        Dictionary<string, string[]> errors,
        string field,
        string label,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[field] = [$"{label} is required."];
            return;
        }

        CheckLength(errors, field, label, value, maxLength);
    }

    /// <summary>
    /// Requires a value to be present but permits it to be blank — for the <c>nvarchar(max)</c>
    /// columns marked required, where null is the only thing the database refuses.
    /// </summary>
    /// <remarks>
    /// Blank is deliberately allowed. A post whose summary is empty and a page saved before its
    /// content is written both store fine today, so rejecting them would be a new editorial rule
    /// smuggled in with a bug fix. Null is nonetheless reachable despite the non-nullable parameter
    /// on the request record: a JSON <c>null</c>, or an omitted member, deserializes straight
    /// through a positional record parameter with nothing checking it.
    /// </remarks>
    public static void CheckPresent(
        Dictionary<string, string[]> errors,
        string field,
        string label,
        string? value)
    {
        if (value is null)
            errors[field] = [$"{label} is required."];
    }

    /// <summary>Bounds a value's length, ignoring null — for optional columns.</summary>
    public static void CheckLength(
        Dictionary<string, string[]> errors,
        string field,
        string label,
        string? value,
        int maxLength)
    {
        if (value is not null && value.Length > maxLength)
            errors[field] = [$"{label} must be {maxLength} characters or fewer."];
    }
}
