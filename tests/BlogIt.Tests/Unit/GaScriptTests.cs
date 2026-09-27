using BlogIt.Components.Shared;
using BlogIt.Services;
using BlogIt.Shared;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace BlogIt.Tests.Unit;

/// <summary>
/// What <see cref="GaScript"/> actually puts in the head. The component contributes through
/// <c>BlogItHeadTags</c> and so renders no markup of its own — these tests pull the registered
/// fragment back out of the head registry and render that, which is the markup a page ends up
/// serving.
/// </summary>
public class GaScriptTests
{
    private static (BunitContext Ctx, IBlogItHeadRegistry Registry) NewContext(string? storedContainerId)
    {
        var ctx = new BunitContext();
        ctx.Services.AddScoped<IBlogItHeadRegistry, BlogItHeadRegistry>();

        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.GetAsync(SettingKeys.GoogleTagManagerContainerId))
            .ReturnsAsync(storedContainerId);
        ctx.Services.AddSingleton(settings.Object);

        return (ctx, ctx.Services.GetRequiredService<IBlogItHeadRegistry>());
    }

    /// <summary>The head markup the component contributed, or empty when it contributed none.</summary>
    private static string HeadMarkup(string? storedContainerId)
    {
        var (ctx, registry) = NewContext(storedContainerId);
        using (ctx)
        {
            ctx.Render<GaScript>();

            var content = registry.Slots.SingleOrDefault()?.Content;
            return content is null ? "" : ctx.Render((RenderFragment)content).Markup;
        }
    }

    [Fact]
    public void LoadsTheContainerFromTheStoredId()
    {
        var markup = HeadMarkup("GTM-WQCQZBKK");

        // The exact element Google's snippet injects, rendered directly instead.
        markup.Should().Contain(
            "src=\"https://www.googletagmanager.com/gtm.js?id=GTM-WQCQZBKK\"");
        markup.Should().Contain("async");

        // gtag.js is a different product and a different endpoint; loading it would bypass the
        // container the consent configuration lives in.
        markup.Should().NotContain("gtag/js");
    }

    [Fact]
    public void BootstrapsTheDataLayerBeforeLoadingTheContainer()
    {
        var markup = HeadMarkup("GTM-WQCQZBKK");

        markup.Should().Contain("dataLayer");
        markup.Should().Contain("gtm.start");

        // Order matters: the container reads the queue the moment it loads, so a push that lands
        // after the loader element can miss the Container Loaded triggers it is there to fire.
        markup.IndexOf("gtm.start", StringComparison.Ordinal)
            .Should().BeLessThan(markup.IndexOf("gtm.js?id=", StringComparison.Ordinal));
    }

    [Fact]
    public void TrimsSurroundingWhitespaceFromTheStoredId()
    {
        HeadMarkup("  GTM-WQCQZBKK  ")
            .Should().Contain("gtm.js?id=GTM-WQCQZBKK\"");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmitsNothingWithNoContainerConfigured(string? storedContainerId)
    {
        HeadMarkup(storedContainerId).Should().BeEmpty();
    }

    [Theory]
    [InlineData("G-ABC123")]
    [InlineData("WQCQZBKK")]
    public void EmitsNothingForAnIdThatIsNotAContainerId(string storedContainerId)
    {
        // Both write paths reject these, so this only catches a value written before the rule
        // existed or straight into the database. Emitting it would cost a request and produce a tag
        // that looks present and never fires — silence is the more honest failure.
        HeadMarkup(storedContainerId).Should().BeEmpty();
    }
}
