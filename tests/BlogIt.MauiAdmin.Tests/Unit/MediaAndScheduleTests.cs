using BlogIt.MauiAdmin.Core.Media;
using BlogIt.MauiAdmin.Core.Publishing;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Uploading from the editor and scheduling are the two flows the client exists for on a phone.
/// Both are easy to break silently — a wrong Markdown shape still saves, and a mishandled
/// DateTimeKind still publishes, just at the wrong hour.
/// </summary>
public class MediaAndScheduleTests
{
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("IMAGE/PNG")]
    public void ImagesAreEmbedded(string contentType)
    {
        MediaMarkdown.ForMedia("Sunset", "/media/sunset.jpg", contentType)
            .Should().Be("![Sunset](/media/sunset.jpg)");
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("video/mp4")]
    public void EverythingElseIsLinked(string contentType)
    {
        MediaMarkdown.ForMedia("Menu", "/media/menu.pdf", contentType)
            .Should().Be("[Menu](/media/menu.pdf)");
    }

    [Fact]
    public void MediaPathsStayRelative()
    {
        MediaMarkdown.ForMedia("Sunset", "/media/sunset.jpg", "image/jpeg")
            .Should().NotContain("http",
                "an absolute URL would pin the post to whichever blog was active when it was written");
    }

    [Fact]
    public void MarkdownIsInsertedAtTheCursor()
    {
        MediaMarkdown.InsertAt("Hello  world", "IMG", 6).Should().Be("Hello IMG world");
    }

    [Fact]
    public void AnOutOfRangeCursorIsClampedRatherThanThrowing()
    {
        MediaMarkdown.InsertAt("Hello", "IMG", 99).Should().Be("HelloIMG");
        MediaMarkdown.InsertAt("Hello", "IMG", -5).Should().Be("IMGHello",
            "an editor that has never had focus reports -1, and that must not crash the insert");
    }

    [Fact]
    public void InsertingIntoAnEmptyDraftWorks()
    {
        MediaMarkdown.InsertAt("", "IMG", 0).Should().Be("IMG");
    }

    [Theory]
    [InlineData("photo.jpg")]
    [InlineData("clip.MP4")]
    [InlineData("menu.pdf")]
    public void ExpectedFileTypesAreAccepted(string fileName)
    {
        MediaUploadPolicy.Validate(fileName, 1024).Should().BeNull();
    }

    [Theory]
    [InlineData("script.js")]
    [InlineData("payload.html")]
    [InlineData("archive.zip")]
    public void OtherFileTypesAreRejected(string fileName)
    {
        MediaUploadPolicy.Validate(fileName, 1024).Should().NotBeNull(
            "the server does not validate uploads, so this is the only check one gets");
    }

    [Fact]
    public void OversizedFilesAreRejectedWithTheLimitInTheMessage()
    {
        var error = MediaUploadPolicy.Validate("photo.jpg", MediaUploadPolicy.MaxSizeBytes + 1);

        error.Should().NotBeNull().And.Contain("50");
    }

    [Fact]
    public void AFileExactlyAtTheLimitIsAccepted()
    {
        MediaUploadPolicy.Validate("photo.jpg", MediaUploadPolicy.MaxSizeBytes).Should().BeNull();
    }

    [Fact]
    public void ADisabledScheduleSendsNothing()
    {
        ScheduleFields.ToUtc(enabled: false, new DateTime(2026, 8, 21), new TimeSpan(9, 0, 0))
            .Should().BeNull();
    }

    [Fact]
    public void APickedDateAndTimeBecomeAUtcInstant()
    {
        var utc = ScheduleFields.ToUtc(enabled: true, new DateTime(2026, 8, 21), new TimeSpan(9, 30, 0));

        utc.Should().NotBeNull();
        utc!.Value.Offset.Should().Be(TimeSpan.Zero, "every instant BlogIt takes is at offset zero");

        var expected = new DateTimeOffset(
            DateTime.SpecifyKind(new DateTime(2026, 8, 21, 9, 30, 0), DateTimeKind.Local))
            .ToUniversalTime();
        utc.Value.Should().Be(expected, "the user picked a time in their own timezone");
    }

    [Fact]
    public void AScheduleSurvivesTheRoundTripBackIntoThePickers()
    {
        var date = new DateTime(2026, 8, 21);
        var time = new TimeSpan(9, 30, 0);

        var utc = ScheduleFields.ToUtc(enabled: true, date, time);
        var restored = ScheduleFields.FromUtc(utc);

        restored.Should().NotBeNull();
        restored!.Value.Date.Should().Be(date);
        restored.Value.Time.Should().Be(time,
            "reopening a scheduled post must show the same time it was saved with");
    }

    [Fact]
    public void NothingScheduledRestoresAsNothing()
    {
        ScheduleFields.FromUtc(null).Should().BeNull();
    }
}
