namespace BlogIt.Shared.DTOs;

public enum PublicationScheduleState
{
    Draft,
    ScheduledForPublishing,
    Published,
    ScheduledForUnpublishing
}

public record UpdatePublicationScheduleRequest(
    DateTime? ScheduledPublishAt,
    DateTime? ScheduledUnpublishAt);

public static class PublicationSchedule
{
    public static PublicationScheduleState GetState(
        bool isPublished,
        DateTime? scheduledPublishAt,
        DateTime? scheduledUnpublishAt)
    {
        if (isPublished)
            return scheduledUnpublishAt.HasValue
                ? PublicationScheduleState.ScheduledForUnpublishing
                : PublicationScheduleState.Published;

        return scheduledPublishAt.HasValue
            ? PublicationScheduleState.ScheduledForPublishing
            : PublicationScheduleState.Draft;
    }

    public static string? Validate(DateTime? scheduledPublishAt, DateTime? scheduledUnpublishAt)
    {
        if (scheduledPublishAt is { Kind: not DateTimeKind.Utc })
            return "Scheduled publish time must be a UTC instant.";
        if (scheduledUnpublishAt is { Kind: not DateTimeKind.Utc })
            return "Scheduled unpublish time must be a UTC instant.";
        if (scheduledPublishAt.HasValue &&
            scheduledUnpublishAt.HasValue &&
            scheduledUnpublishAt <= scheduledPublishAt)
            return "Scheduled unpublish time must be after the scheduled publish time.";

        return null;
    }
}
