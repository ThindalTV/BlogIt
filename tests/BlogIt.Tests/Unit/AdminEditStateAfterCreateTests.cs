using System.Net;
using AngleSharp.Dom;
using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using PageEditPage = BlogIt.Admin.Pages.Pages.PageEdit;
using PostEditPage = BlogIt.Admin.Pages.Posts.PostEdit;

namespace BlogIt.Tests.Unit;

/// <summary>
/// What the two editors hold after they save a brand-new post or page. Creating navigated to
/// <c>posts/{id}</c>, which routes back to the same component, so Blazor kept the instance and
/// <c>OnInitializedAsync</c> never ran again: the entity DTO stayed null and every branch reading it
/// took the wrong side. The header fell back to "Edit Post", the schedule state line vanished, the
/// slug stayed editable after the server had locked it, "Save Draft" tested
/// <c>post?.IsPublished == true</c> against null and so silently left the post live, and the
/// concurrency token sent on the next save was <see cref="Guid.Empty"/>, which the API deliberately
/// fails closed on.
/// </summary>
public class AdminEditStateAfterCreateTests
{
    private static readonly Guid PostId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PostStamp = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid PageStamp = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly string EmptyStamp = Guid.Empty.ToString();

    // ── Posts ───────────────────────────────────────────────────────────────

    [Fact]
    public void PostEdit_AfterCreatingAndPublishing_RendersTheSavedPostNotAnEmptyForm()
    {
        using var ctx = PostHarness();
        var cut = ctx.Render<PostEditPage>();

        CreatePost(cut);

        cut.Find(".page-title").TextContent.Should().Be("Release notes");
        cut.Markup.Should().Contain("Current state: Published",
            "the scheduling panel's state line only renders when the DTO is loaded");
        cut.Find("#post-slug").HasAttribute("disabled").Should().BeTrue(
            "the first publication locked the slug server-side, so the field must not invite edits");
    }

    /// <summary>
    /// The worst of the four consequences: the user presses a button, sees no error, and the post
    /// stays live. With the DTO null the guard fell through to a plain draft save, which updates the
    /// fields and never calls unpublish.
    /// </summary>
    [Fact]
    public void PostEdit_SaveDraftAfterCreatingAPublishedPost_WarnsAndActuallyUnpublishes()
    {
        using var ctx = PostHarness();
        var cut = ctx.Render<PostEditPage>();
        CreatePost(cut);

        Button(cut, "Save Draft").Click();

        cut.Find(".modal-footer .btn-danger").TextContent.Should().Contain("Unpublish");
        cut.Find(".modal-footer .btn-danger").Click();

        ctx.Requests.Should().Contain(
            r => r.Method == HttpMethod.Post && r.Url.EndsWith($"posts/{PostId}/unpublish"),
            "Save Draft on a live post has to take it offline");
    }

    [Fact]
    public void PostEdit_TheSaveAfterACreate_SendsTheLoadedConcurrencyStamp()
    {
        using var ctx = PostHarness();
        var cut = ctx.Render<PostEditPage>();
        CreatePost(cut);

        Button(cut, "Update & Publish").Click();

        var update = ctx.Requests.Last(r => r.Method == HttpMethod.Put);
        update.Body.Should().Contain(PostStamp.ToString());
        update.Body.Should().NotContain(EmptyStamp,
            "the API's concurrency guard fails closed on an empty token, so a desynced editor turns "
            + "the very next save into a 409 the user cannot clear");
    }

    /// <summary>
    /// The same reload path, reached by pointing the router at a different post while an editor is
    /// already on screen — also a parameter change with no re-initialisation.
    /// </summary>
    [Fact]
    public void PostEdit_NavigatingFromOnePostToAnother_LoadsTheSecondOne()
    {
        var other = Guid.Parse("33333333-3333-3333-3333-333333333333");
        // Both id routes have to be registered ahead of the broad "posts" one, which would otherwise
        // swallow them: the fake server matches on the first fragment that occurs in the URL.
        using var ctx = new AdminComponentHarness()
            .Route($"posts/{other}", Post(other, "The other one", published: false))
            .Route($"posts/{PostId}", Post(PostId, "Release notes", published: true));
        var cut = ctx.Render<PostEditPage>(p => p.Add(x => x.Id, PostId));
        cut.Find(".page-title").TextContent.Should().Be("Release notes");

        // What the router does on a URL change that lands on the same component: new parameters,
        // same instance, no second OnInitializedAsync.
        cut.Render(p => p.Add(x => x.Id, other));

        cut.Find(".page-title").TextContent.Should().Be("The other one");
        cut.Find("#post-slug").HasAttribute("disabled").Should().BeFalse();
    }

    // ── Pages ───────────────────────────────────────────────────────────────

    [Fact]
    public void PageEdit_AfterCreatingAPublishedPage_RendersTheSavedPageNotAnEmptyForm()
    {
        using var ctx = PageHarness();
        var cut = ctx.Render<PageEditPage>();

        CreatePage(cut);

        cut.Find(".page-title").TextContent.Should().Be("About us");
        cut.Markup.Should().Contain("Current state: Published");
        cut.Find("#page-slug").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void PageEdit_TheSaveAfterACreate_SendsTheLoadedConcurrencyStamp()
    {
        using var ctx = PageHarness();
        var cut = ctx.Render<PageEditPage>();
        CreatePage(cut);

        Button(cut, "Save").Click();

        var update = ctx.Requests.Last(r => r.Method == HttpMethod.Put);
        update.Body.Should().Contain(PageStamp.ToString());
        update.Body.Should().NotContain(EmptyStamp);
    }

    // ── The URL ─────────────────────────────────────────────────────────────

    [Fact]
    public void BothEditors_LeaveTheUrlOnTheSavedEntitySoARefreshOrSharedLinkWorks()
    {
        using var postCtx = PostHarness();
        CreatePost(postCtx.Render<PostEditPage>());
        Uri(postCtx).Should().EndWith($"posts/{PostId}");

        using var pageCtx = PageHarness();
        CreatePage(pageCtx.Render<PageEditPage>());
        Uri(pageCtx).Should().EndWith($"pages/{PageId}");
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    private static string Uri(AdminComponentHarness ctx) =>
        ctx.Services.GetRequiredService<NavigationManager>().Uri;

    private static IElement Button<T>(IRenderedComponent<T> cut, string label) where T : IComponent =>
        cut.FindAll(".header-actions button").First(b => b.TextContent.Trim() == label);

    private static void CreatePost<T>(IRenderedComponent<T> cut) where T : IComponent
    {
        cut.Find("#post-title").Change("Release notes");
        cut.Find(".header-actions .btn-primary").Click();
    }

    private static void CreatePage<T>(IRenderedComponent<T> cut) where T : IComponent
    {
        cut.Find("#page-title-field").Change("About us");
        cut.Find("#page-slug").Change("about-us");
        cut.Find(".form-check input").Change(true);
        cut.Find(".header-actions .btn-primary").Click();
    }

    /// <summary>
    /// A server that hands back an unpublished post from the create call and a published one from
    /// every read afterwards — the sequence a "Publish" on a new post produces, and the one that
    /// makes the difference between the created DTO and the current state observable.
    /// </summary>
    private static AdminComponentHarness PostHarness() =>
        new AdminComponentHarness()
            .RouteStatus($"posts/{PostId}/publish", HttpStatusCode.OK)
            .RouteStatus($"posts/{PostId}/unpublish", HttpStatusCode.OK)
            .Route($"posts/{PostId}", Post(PostId, "Release notes", published: true))
            .Route("posts", Post(PostId, "Release notes", published: false));

    /// <summary>
    /// Pages have no separate publish call — <c>isPublished</c> travels with the save — so the create
    /// reply and the read that follows describe the same published page.
    /// </summary>
    private static AdminComponentHarness PageHarness() =>
        new AdminComponentHarness()
            .Route($"pages/{PageId}", Page())
            .Route("pages", Page());

    private static BlogPostDetailDto Post(Guid id, string title, bool published) => new(
        id, title, "release-notes", "What changed", "Body", true,
        published, published ? DateTime.UtcNow : null, DateTime.UtcNow, DateTime.UtcNow,
        Guid.NewGuid(), "Admin", null, null, null, null, [],
        ScheduleState: published ? PublicationScheduleState.Published : PublicationScheduleState.Draft,
        HasBeenPublished: published,
        ConcurrencyStamp: PostStamp);

    private static PageDto Page() => new(
        PageId, "About us", "about-us", "Body", true, DateTime.UtcNow, DateTime.UtcNow,
        null, null, null, null,
        ScheduleState: PublicationScheduleState.Published,
        HasBeenPublished: true,
        ConcurrencyStamp: PageStamp);
}
