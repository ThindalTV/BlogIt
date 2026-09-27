using System.Globalization;

namespace BlogIt.MauiAdmin.Core.Publishing;

/// <summary>
/// The one-line status a post or page shows in a list.
/// </summary>
/// <remarks>
/// Takes local times, not UTC: the caller converts, because only it knows whether the value came
/// from a DTO (UTC) or an editor field (already local), and a helper that guesses would silently
/// shift every displayed time by the machine's offset.
/// </remarks>
public static class PublicationStatusText
{
    /// <summary>Day-month-year with a 24-hour clock, short enough for a list row.</summary>
    private const string Format = "d MMM yyyy HH:mm";

    public static string Describe(bool isPublished, DateTimeOffset? scheduledPublishAt, DateTimeOffset? scheduledUnpublishAt)
    {
        var until = scheduledUnpublishAt is { } end
            ? $" until {end.ToString(Format, CultureInfo.CurrentCulture)}"
            : string.Empty;

        if (isPublished)
            return $"Published{until}";

        // Not live: the publish date is the headline, because it is the difference between a
        // draft nobody has committed to and a post that goes out on Friday.
        return scheduledPublishAt is { } start
            ? $"Scheduled for {start.ToString(Format, CultureInfo.CurrentCulture)}{until}"
            : "Draft";
    }

    /// <summary>
    /// Converts a timestamp from a DTO into the local value <see cref="Describe"/> expects,
    /// preserving null.
    /// </summary>
    public static DateTimeOffset? ToLocal(DateTimeOffset? utc) => utc?.ToLocalTime();
}
