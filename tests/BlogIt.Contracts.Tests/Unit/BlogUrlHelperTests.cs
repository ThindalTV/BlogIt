using BlogIt.Shared.Helpers;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

public class BlogUrlHelperTests
{
    [Fact]
    public void GetPostPath_UsesPublishedYear()
    {
        var path = BlogUrlHelper.GetPostPath(
            "hello-world",
            new DateTime(2025, 12, 31),
            new DateTime(2024, 1, 1));

        path.Should().Be("/2025/hello-world");
    }

    [Fact]
    public void GetPostPath_UsesCreatedYearForDraft()
    {
        var path = BlogUrlHelper.GetPostPath(
            "draft",
            publishedAt: null,
            createdAt: new DateTime(2026, 8, 4));

        path.Should().Be("/2026/draft");
    }
}
