using System.Globalization;
using BlogIt.MauiAdmin.Core.Publishing;
using BlogIt.Tests.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The post and page lists used to render nothing but "Published" or "Draft", even though the
/// list could be filtered to "scheduled" and the DTOs carry both schedule timestamps. A queued
/// post was therefore indistinguishable from a draft that would never go anywhere: the only way
/// to see what was scheduled was to open every post in turn.
/// </summary>
public class PublicationStatusTextTests
{
    private static readonly DateTime Publish = new(2026, 8, 21, 9, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Unpublish = new(2026, 9, 3, 17, 30, 0, DateTimeKind.Local);

    /// <summary>
    /// How the subject renders a date, asked of the same culture it uses. Hard-coding "3 Sep 2026"
    /// here would assert the current ICU abbreviations rather than the behaviour under test — the
    /// running culture renders September as "Sept".
    /// </summary>
    private static string Rendered(DateTime value) =>
        value.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture);

    [Fact]
    public void ADraftWithNoScheduleSaysDraft()
    {
        PublicationStatusText.Describe(isPublished: false, null, null).Should().Be("Draft");
    }

    [Fact]
    public void ALivePostWithNoScheduleSaysPublished()
    {
        PublicationStatusText.Describe(isPublished: true, null, null).Should().Be("Published");
    }

    [Fact]
    public void AQueuedPostShowsWhenItGoesLive()
    {
        var text = PublicationStatusText.Describe(isPublished: false, Publish, null);

        text.Should().StartWith("Scheduled", "a queued post must not read as an ordinary draft");
        text.Should().Contain(Rendered(Publish));
    }

    [Fact]
    public void ALivePostWithAnEndDateShowsWhenItComesDown()
    {
        var text = PublicationStatusText.Describe(isPublished: true, null, Unpublish);

        text.Should().StartWith("Published");
        text.Should().Contain("until").And.Contain(Rendered(Unpublish));
    }

    [Fact]
    public void AQueuedPostWithBothDatesShowsTheWindow()
    {
        var text = PublicationStatusText.Describe(isPublished: false, Publish, Unpublish);

        text.Should().Contain(Rendered(Publish)).And.Contain(Rendered(Unpublish),
            "both ends of the window matter when nothing is live yet");
    }

    [Fact]
    public void BothListsReloadWhenTheStatusFilterChanges()
    {
        foreach (var (area, viewModel) in new[]
                 {
                     ("Posts", "PostListViewModel.cs"),
                     ("Pages", "PageListViewModel.cs"),
                 })
        {
            var source = File.ReadAllText(RepoLayout.Combine(
                "src", "BlogIt.MauiAdmin", "ViewModels", area, viewModel));

            source.Should().Contain("OnStatusFilterChanged",
                $"{area}: picking a status in the filter must reload the list — without a change " +
                "handler the picker looks like it works but nothing happens until the search box " +
                "is re-submitted");
        }
    }
}
