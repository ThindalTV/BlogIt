namespace BlogIt.MauiAdmin.Core.Publishing;

/// <summary>
/// When the editor should offer each publishing control, given the state of the post or page
/// being edited.
/// </summary>
/// <remarks>
/// These read as obvious one-liners, which is the point: they were previously implicit in XAML
/// binding expressions, where the scheduled-publish switch was bound to the inverse of the slug
/// lock. That quietly made "this post has been published before" mean "this post can never be
/// scheduled again", so unpublishing something was a one-way door. Stating each rule in terms of
/// the state it actually depends on keeps unrelated facts from being conflated again.
/// </remarks>
public static class PublishingRules
{
    /// <summary>
    /// Whether a publish date can be set. Anything not currently live can be scheduled to go
    /// live — including a post that was published before and has since been taken offline.
    /// </summary>
    public static bool CanSchedulePublish(bool isPublished, bool hasBeenPublished) => !isPublished;

    /// <summary>
    /// Whether an unpublish date can be set: only for content that is live, or that is going to
    /// be, so an end date can be set at the same time as the start date.
    /// </summary>
    public static bool CanScheduleUnpublish(bool isPublished, bool hasScheduledPublish) =>
        isPublished || hasScheduledPublish;

    /// <summary>
    /// Whether the slug is fixed. It locks on first publication and stays locked forever after,
    /// because the published URL is out in the world — in feeds, in links, in search results —
    /// and unpublishing does not call those back.
    /// </summary>
    public static bool IsSlugLocked(bool hasBeenPublished) => hasBeenPublished;
}
