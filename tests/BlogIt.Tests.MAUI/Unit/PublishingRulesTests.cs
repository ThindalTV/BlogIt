using BlogIt.MauiAdmin.Core.Publishing;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Unpublishing and republishing is a first-class flow, including scheduling the republish for
/// a future date. The editor used to forbid it: the scheduled-publish switch was disabled
/// whenever the slug was locked, and the slug locks on first publication — so a post that had
/// ever been live could never be scheduled again, even after being taken down. Those are two
/// unrelated facts about a post and they are kept apart here.
/// </summary>
public class PublishingRulesTests
{
    [Fact]
    public void APostTakenOfflineCanBeScheduledToGoLiveAgain()
    {
        PublishingRules.CanSchedulePublish(isPublished: false, hasBeenPublished: true)
            .Should().BeTrue("republishing on a schedule is a normal thing to want after unpublishing");
    }

    [Fact]
    public void ADraftCanBeScheduledToPublish()
    {
        PublishingRules.CanSchedulePublish(isPublished: false, hasBeenPublished: false)
            .Should().BeTrue();
    }

    [Fact]
    public void ALivePostCannotBeScheduledToPublish()
    {
        PublishingRules.CanSchedulePublish(isPublished: true, hasBeenPublished: true)
            .Should().BeFalse("it is already live — scheduling it to publish would mean nothing");
    }

    [Fact]
    public void OnlyContentThatWillBeLiveCanBeScheduledToUnpublish()
    {
        PublishingRules.CanScheduleUnpublish(isPublished: true, hasScheduledPublish: false)
            .Should().BeTrue();
        PublishingRules.CanScheduleUnpublish(isPublished: false, hasScheduledPublish: true)
            .Should().BeTrue("a post scheduled to go live can also be given an end date up front");
        PublishingRules.CanScheduleUnpublish(isPublished: false, hasScheduledPublish: false)
            .Should().BeFalse("a draft with no publish date has nothing to take offline");
    }

    [Fact]
    public void SlugLockingIsIndependentOfScheduling()
    {
        PublishingRules.IsSlugLocked(hasBeenPublished: true).Should().BeTrue(
            "the slug is a published URL and must not move under readers");
        PublishingRules.IsSlugLocked(hasBeenPublished: false).Should().BeFalse();
    }

    [Fact]
    public void TheEditorDoesNotGateSchedulingOnTheSlugLock()
    {
        var postEditor = File.ReadAllText(RepoLayout.Combine(
            "src", "BlogIt.MauiAdmin", "Views", "Posts", "PostEditPage.xaml"));

        postEditor.Should().NotContain("IsToggled=\"{Binding SchedulePublishEnabled}\" IsEnabled=\"{Binding SlugLocked",
            "the slug lock says the URL is fixed, not that the post can never be scheduled again");
        postEditor.Should().Contain("CanSchedulePublish",
            "the scheduled-publish switch should follow the publishing rule instead");
    }
}
