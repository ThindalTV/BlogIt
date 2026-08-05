using BlogIt.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace BlogIt.Tests.Unit;

public class PreviewTokenServiceTests
{
    [Fact]
    public void TryAuthorize_ExchangesSingleUseTokenForRefreshCookie()
    {
        var service = new PreviewTokenService(new TestTimeProvider());
        var contentId = Guid.NewGuid();
        var (token, _) = service.Issue(PreviewContentType.Post, contentId);
        var initialRequest = CreateContext("/2026/draft");

        service.TryAuthorize(initialRequest, token, PreviewContentType.Post, contentId)
            .Should().BeTrue();

        var replayRequest = CreateContext("/2026/draft");
        service.TryAuthorize(replayRequest, token, PreviewContentType.Post, contentId)
            .Should().BeFalse();

        var refreshRequest = CreateContext("/2026/draft");
        refreshRequest.Request.Headers.Cookie =
            initialRequest.Response.Headers.SetCookie.ToString().Split(';')[0];
        service.TryAuthorize(refreshRequest, token, PreviewContentType.Post, contentId)
            .Should().BeTrue();
    }

    [Fact]
    public void TryAuthorize_RejectsTokenAfterFifteenMinutes()
    {
        var time = new TestTimeProvider();
        var service = new PreviewTokenService(time);
        var contentId = Guid.NewGuid();
        var (token, expiresAt) = service.Issue(PreviewContentType.Page, contentId);

        expiresAt.Should().Be(time.GetUtcNow().AddMinutes(15));
        time.Advance(TimeSpan.FromMinutes(15));

        service.TryAuthorize(
                CreateContext("/private"),
                token,
                PreviewContentType.Page,
                contentId)
            .Should().BeFalse();
    }

    private static DefaultHttpContext CreateContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset utcNow = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
    }
}
