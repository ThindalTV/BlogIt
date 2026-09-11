namespace BlogIt.Shared.DTOs;

public enum PublicationScheduleState
{
    Draft,
    ScheduledForPublishing,
    Published,
    ScheduledForUnpublishing
}

public record UpdatePublicationScheduleRequest(
    DateTimeOffset? ScheduledPublishAt,
    DateTimeOffset? ScheduledUnpublishAt);

public static class PublicationSchedule
{
    public static PublicationScheduleState GetState(
        bool isPublished,
        DateTimeOffset? scheduledPublishAt,
        DateTimeOffset? scheduledUnpublishAt)
    {
        if (isPublished)
            return scheduledUnpublishAt.HasValue
                ? PublicationScheduleState.ScheduledForUnpublishing
                : PublicationScheduleState.Published;

        return scheduledPublishAt.HasValue
            ? PublicationScheduleState.ScheduledForPublishing
            : PublicationScheduleState.Draft;
    }

    /// <remarks>
    /// There is no longer a kind check here. These were <see cref="DateTime"/>, which could arrive
    /// labelled <c>Unspecified</c> or <c>Local</c> and so had to be rejected outright to avoid
    /// storing a wall-clock reading as an instant. A <see cref="DateTimeOffset"/> always names an
    /// unambiguous instant whatever offset it carries, so ordering is the only rule left.
    /// </remarks>
    public static string? Validate(
        DateTimeOffset? scheduledPublishAt,
        DateTimeOffset? scheduledUnpublishAt)
    {
        if (scheduledPublishAt.HasValue &&
            scheduledUnpublishAt.HasValue &&
            scheduledUnpublishAt <= scheduledPublishAt)
            return "Scheduled unpublish time must be after the scheduled publish time.";

        return null;
    }
}
