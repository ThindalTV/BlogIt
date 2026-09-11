using BlogIt.Components.Shared;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Blazor's head section keeps only the <em>last</em> <c>&lt;HeadContent&gt;</c> that renders, so two
/// components each declaring their own block do not combine — the second silently replaces the
/// first. Every public page rendered both <c>SeoHead</c> and <c>GaScript</c>, so configuring a
/// Google Analytics measurement ID removed every Open Graph, Twitter, canonical and structured-data
/// tag from the site, and the same mechanism dropped the <c>noindex</c> from draft previews.
/// <para>
/// The replacement routes every contribution through one registry that a single
/// <see cref="BlogItHeadOutlet"/> renders. These tests cover the registry and the contributing
/// component; that the composed tags reach the served <c>&lt;head&gt;</c> is verified against the
/// running sample, since it depends on the real head outlet rather than on bunit's renderer.
/// </para>
/// </summary>
public class BlogItHeadCompositionTests
{
    private static BunitContext NewContext()
    {
        var ctx = new BunitContext();
        ctx.Services.AddScoped<IBlogItHeadRegistry, BlogItHeadRegistry>();
        return ctx;
    }

    private static RenderFragment Meta(string name) => builder =>
    {
        builder.OpenElement(0, "meta");
        builder.AddAttribute(1, "name", name);
        builder.CloseElement();
    };

    [Fact]
    public void EveryContributorSurvives_NotJustTheLastOne()
    {
        using var ctx = NewContext();
        var registry = ctx.Services.GetRequiredService<IBlogItHeadRegistry>();

        // Three components each contributing their own block — SEO, analytics and the preview
        // robots meta is exactly the combination that used to lose two of the three.
        foreach (var name in new[] { "seo", "analytics", "robots" })
            ctx.Render<BlogItHeadTags>(p => p.Add(x => x.ChildContent, Meta(name)));

        registry.Slots.Should().HaveCount(3);
        registry.Slots.Should().OnlyContain(slot => slot.Content != null);
    }

    [Fact]
    public void ContributionsKeepTheOrderTheyWereRegisteredIn()
    {
        using var ctx = NewContext();
        var registry = ctx.Services.GetRequiredService<IBlogItHeadRegistry>();
        var first = Meta("first");
        var second = Meta("second");

        ctx.Render<BlogItHeadTags>(p => p.Add(x => x.ChildContent, first));
        ctx.Render<BlogItHeadTags>(p => p.Add(x => x.ChildContent, second));

        registry.Slots.Select(slot => slot.Content).Should().Equal(first, second);
    }

    [Fact]
    public void AContributorRendersNoMarkupOfItsOwn()
    {
        using var ctx = NewContext();

        var cut = ctx.Render<BlogItHeadTags>(p => p.Add(x => x.ChildContent, Meta("seo")));

        cut.Markup.Should().BeEmpty("the tags belong in the head, not where the component sits");
    }

    [Fact]
    public void ReRenderingAContributorUpdatesItsEntryInsteadOfAddingAnother()
    {
        using var ctx = NewContext();
        var registry = ctx.Services.GetRequiredService<IBlogItHeadRegistry>();
        var updated = Meta("updated");

        var cut = ctx.Render<BlogItHeadTags>(p => p.Add(x => x.ChildContent, Meta("original")));
        cut.Render(p => p.Add(x => x.ChildContent, updated));

        registry.Slots.Should().ContainSingle("a re-render must not append a duplicate entry");
        registry.Slots[0].Content.Should().Be(updated);
    }

    [Fact]
    public void ADisposedContributorStopsContributing()
    {
        using var ctx = NewContext();
        var registry = ctx.Services.GetRequiredService<IBlogItHeadRegistry>();

        var cut = ctx.Render<BlogItHeadTags>(p => p.Add(x => x.ChildContent, Meta("seo")));
        registry.Slots.Should().ContainSingle();

        cut.Instance.Dispose();

        registry.Slots.Should().BeEmpty();
    }

    [Fact]
    public void LateRegistrationRaisesChanged_SoTheOutletCanReRender()
    {
        // Pages initialise their components in their own order, and any of them may await settings
        // before deciding what to contribute — so the outlet routinely renders before the tags exist.
        using var ctx = NewContext();
        var registry = ctx.Services.GetRequiredService<IBlogItHeadRegistry>();
        var notifications = 0;
        registry.Changed += () => notifications++;

        var slot = new BlogItHeadSlot { Content = Meta("late") };
        registry.Add(slot);
        registry.NotifyChanged();
        registry.Remove(slot);

        notifications.Should().Be(3);
    }

    [Fact]
    public void TheRegistryIsPerRequest_NotSharedBetweenThem()
    {
        // Registered scoped rather than singleton on purpose: the head belongs to one page render.
        // A shared instance would append one visitor's meta tags onto another's concurrent page and
        // grow without bound for the lifetime of the process.
        using var services = new ServiceCollection()
            .AddScoped<IBlogItHeadRegistry, BlogItHeadRegistry>()
            .BuildServiceProvider();

        using var requestOne = services.CreateScope();
        using var requestTwo = services.CreateScope();

        var first = requestOne.ServiceProvider.GetRequiredService<IBlogItHeadRegistry>();
        var second = requestTwo.ServiceProvider.GetRequiredService<IBlogItHeadRegistry>();

        first.Should().NotBeSameAs(second);
        first.Add(new BlogItHeadSlot { Content = Meta("from-request-one") });

        second.Slots.Should().BeEmpty("one request's head tags must not leak into another's");
    }
}
