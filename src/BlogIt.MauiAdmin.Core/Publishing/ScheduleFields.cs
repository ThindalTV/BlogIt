namespace BlogIt.MauiAdmin.Core.Publishing;

/// <summary>
/// Converts between the editor's separate date and time pickers and the single instant the API
/// takes.
/// </summary>
/// <remarks>
/// The pickers deal in <see cref="DateTime"/> and <see cref="TimeSpan"/> because that is what
/// MAUI's DatePicker and TimePicker bind to, and they hand back
/// <see cref="DateTimeKind.Unspecified"/> values that mean "whatever the user sees on their own
/// clock". The API deals in <see cref="DateTimeOffset"/>. This is the one place that crossing is
/// made explicit, and it is tested, because getting it wrong is invisible — a post would go live
/// at the right-looking wrong hour.
/// </remarks>
public static class ScheduleFields
{
    /// <summary>The instant for a date and time the user picked on their own clock, or null when
    /// that schedule is switched off.</summary>
    public static DateTimeOffset? ToUtc(bool enabled, DateTime date, TimeSpan time)
    {
        if (!enabled) return null;

        // Stamping Local before the conversion is what pins the picker's "unspecified" value to
        // the user's offset; DateTimeOffset would otherwise be free to read it as UTC.
        var local = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Local);
        return new DateTimeOffset(local).ToUniversalTime();
    }

    /// <summary>Splits an instant from the API back into the local date and time the pickers
    /// show. Returns null when nothing is scheduled.</summary>
    public static (DateTime Date, TimeSpan Time)? FromUtc(DateTimeOffset? utc)
    {
        if (utc is not { } value) return null;

        var local = value.ToLocalTime();
        return (local.Date, local.TimeOfDay);
    }
}
