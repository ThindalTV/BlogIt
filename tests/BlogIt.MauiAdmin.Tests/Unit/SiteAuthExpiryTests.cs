using BlogIt.MauiAdmin.Core.Sites;
using FluentAssertions;

namespace BlogIt.Tests.Unit;

/// <summary>
/// A 401 signs a site out and sends the user to its login screen. These pin that it signs out the
/// right site, and only once.
/// </summary>
public class SiteAuthExpiryTests
{
    private static readonly SiteProfile Travel = new() { Id = "travel", Host = "travel.example" };
    private static readonly SiteProfile Food = new() { Id = "food", Host = "food.example" };
    private static readonly SiteProfile[] Sites = [Travel, Food];

    [Fact]
    public void TheSiteIsTheOneTheRequestWasSentTo()
    {
        // Not the active one: switching sites while a request was in flight used to sign out the
        // site switched to, because the handler asked for the active profile after the response.
        SiteAuthExpiry.ProfileForRequest(new Uri("https://food.example/api/posts"), Sites)
            .Should().BeSameAs(Food);
    }

    [Fact]
    public void ARequestToNoKnownSiteMatchesNothing()
    {
        SiteAuthExpiry.ProfileForRequest(new Uri("https://elsewhere.example/api/posts"), Sites)
            .Should().BeNull();
        SiteAuthExpiry.ProfileForRequest(new Uri("https://food.example/media/photo.jpg"), Sites)
            .Should().BeNull("a URL outside the API root is not an API request for that site");
        SiteAuthExpiry.ProfileForRequest(null, Sites).Should().BeNull();
    }

    [Fact]
    public void TwoSitesOnOneHostAreToldApartByTheirApiPath()
    {
        var root = new SiteProfile { Id = "root", Host = "example.com", ApiPathOverride = "/api" };
        var blog = new SiteProfile { Id = "blog", Host = "example.com", ApiPathOverride = "/api/blog" };

        SiteAuthExpiry.ProfileForRequest(new Uri("https://example.com/api/blog/posts"), [root, blog])
            .Should().BeSameAs(blog, "the most specific API root wins");
        SiteAuthExpiry.ProfileForRequest(new Uri("https://example.com/api/posts"), [root, blog])
            .Should().BeSameAs(root);
    }

    [Fact]
    public void ADifferentPortOrSchemeIsADifferentSite()
    {
        var dev = new SiteProfile { Id = "dev", Host = "localhost", Port = 5001 };

        SiteAuthExpiry.ProfileForRequest(new Uri("https://localhost:5002/api/posts"), [dev])
            .Should().BeNull();
        SiteAuthExpiry.ProfileForRequest(new Uri("http://localhost:5001/api/posts"), [dev])
            .Should().BeNull();
    }

    [Fact]
    public void A401ForTheStoredTokenEndsTheSession() =>
        SiteAuthExpiry.ShouldExpire("token-a", "token-a").Should().BeTrue();

    [Fact]
    public void OnlyTheFirstOfSeveralConcurrent401sActs() =>
        // The first clears the token; every later one finds nothing stored.
        SiteAuthExpiry.ShouldExpire("token-a", null).Should().BeFalse();

    [Fact]
    public void A401ForATokenSinceReplacedBySigningInAgainIsIgnored() =>
        SiteAuthExpiry.ShouldExpire("token-a", "token-b").Should().BeFalse();

    [Fact]
    public void A401SentWithoutATokenEndsAStoredButExpiredSession() =>
        // The client omits the header once the stored token is past its expiry, so the request
        // carries nothing to compare - but the session is over all the same.
        SiteAuthExpiry.ShouldExpire(null, "token-a").Should().BeTrue();

    [Fact]
    public void A401SentWithoutATokenAfterSignOutIsIgnored() =>
        SiteAuthExpiry.ShouldExpire(null, null).Should().BeFalse();
}
