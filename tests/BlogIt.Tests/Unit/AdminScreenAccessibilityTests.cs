using BlogIt.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

using ChangePasswordPage = BlogIt.Admin.Pages.Account.ChangePassword;
using ConversationChatPage = BlogIt.Admin.Pages.Ai.ConversationChat;
using ConversationListPage = BlogIt.Admin.Pages.Ai.ConversationList;
using DashboardPage = BlogIt.Admin.Pages.Dashboard;
using LoginPage = BlogIt.Admin.Pages.Login;
using MediaListPage = BlogIt.Admin.Pages.Media.MediaList;
using PageEditPage = BlogIt.Admin.Pages.Pages.PageEdit;
using PageListPage = BlogIt.Admin.Pages.Pages.PageList;
using PostEditPage = BlogIt.Admin.Pages.Posts.PostEdit;
using PostListPage = BlogIt.Admin.Pages.Posts.PostList;
using RedirectListPage = BlogIt.Admin.Pages.Redirects.RedirectList;
using SetupPage = BlogIt.Admin.Pages.Setup;
using SiteSettingsPage = BlogIt.Admin.Pages.Settings.SiteSettings;
using UserListPage = BlogIt.Admin.Pages.Users.UserList;

namespace BlogIt.Tests.Unit;

/// <summary>
/// Every admin screen, rendered and swept for the two rules that used to hold nowhere: a control
/// the user can name, and a click target the keyboard can reach. Written as a sweep per screen
/// rather than an assertion per field so that a field added later without a label fails here.
/// </summary>
/// <remarks>
/// The admin had 60 <c>form-label</c> elements, all of them bare <c>&lt;label&gt;</c> with no
/// <c>for</c> and no matching <c>id</c> anywhere — password boxes included, which also stops a
/// password manager recognising them. Alongside that, selection in the media grids and the chat
/// rename were <c>div</c>s carrying <c>@onclick</c>, and Upload was a <c>&lt;label&gt;</c> around a
/// <c>display:none</c> input, which is not focusable — so uploading could not be started from the
/// keyboard at all.
/// </remarks>
public class AdminScreenAccessibilityTests
{
    private static void Sweep<T>(IRenderedComponent<T> cut, string screen) where T : IComponent
    {
        AdminAccessibility.AssertControlsAreLabelled(cut, screen);
        AdminAccessibility.AssertClickTargetsAreControls(cut, screen);
    }

    // ── Auth and setup ──────────────────────────────────────────────────────

    [Fact]
    public void Login_NamesItsUsernameAndPasswordBoxes()
    {
        using var ctx = new AdminComponentHarness()
            .Route("setup/status", new SetupStatusResponse(true));

        Sweep(ctx.Render<LoginPage>(), "Login");
    }

    [Fact]
    public void Setup_NamesEveryFieldOnEveryWizardStep()
    {
        using var ctx = new AdminComponentHarness()
            .Route("setup/status", new SetupStatusResponse(false));

        var cut = ctx.Render<SetupPage>();

        Sweep(cut, "Setup step 1 (admin account)");
        // Positional rather than by id: the ids are what these tests are asserting about, so
        // driving the wizard through them would make the fixture pass by construction.
        cut.FindAll(".wizard-section input")[0].Change("admin");
        cut.FindAll(".wizard-section input")[2].Change("correct-horse");
        cut.FindAll(".wizard-section input")[3].Change("correct-horse");
        Next(cut);

        Sweep(cut, "Setup step 2 (site information)");
        cut.FindAll(".wizard-section input")[0].Change("My Blog");
        cut.FindAll(".wizard-section input")[1].Change("https://example.com");
        Next(cut);

        // The OpenAI-compatible branch is the one that renders Base URL, Model and Export Model.
        cut.Find(".wizard-section select").Change("openai-compatible");
        Sweep(cut, "Setup step 3 (AI provider)");
        Next(cut);

        Sweep(cut, "Setup step 4 (analytics)");
    }

    [Fact]
    public void ChangePassword_NamesAllThreePasswordBoxes()
    {
        using var ctx = new AdminComponentHarness();

        Sweep(ctx.Render<ChangePasswordPage>(), "ChangePassword");
    }

    // ── Settings ────────────────────────────────────────────────────────────

    [Fact]
    public void SiteSettings_NamesEveryFieldIncludingTheSecrets()
    {
        using var ctx = new AdminComponentHarness()
            .Route("settings", new Dictionary<string, string>
            {
                [SettingKeys.SiteName] = "My Blog",
                [SettingKeys.SiteUrl] = "https://example.com",
                // Renders the conditional Base URL / Model / Export Model group.
                [SettingKeys.AiProvider] = "openai-compatible",
            });

        Sweep(ctx.Render<SiteSettingsPage>(), "SiteSettings");
    }

    // ── Lists with inline create forms ──────────────────────────────────────

    [Fact]
    public void UserList_NamesTheNewUserFormIncludingThePassword()
    {
        using var ctx = new AdminComponentHarness()
            .Route("users", Array.Empty<AppUserDto>());

        var cut = ctx.Render<UserListPage>();
        cut.Find(".page-header button").Click();

        Sweep(cut, "UserList (new user form open)");
    }

    [Fact]
    public void RedirectList_NamesTheRedirectForm()
    {
        using var ctx = new AdminComponentHarness()
            .Route("redirects", Array.Empty<UrlRedirectDto>());

        var cut = ctx.Render<RedirectListPage>();
        cut.Find(".page-header button").Click();

        Sweep(cut, "RedirectList (form open)");
    }

    [Fact]
    public void PostList_NamesItsSearchBoxAndStatusFilter()
    {
        using var ctx = new AdminComponentHarness()
            .Route("posts", AdminComponentHarness.OnePage(Post()));

        Sweep(ctx.Render<PostListPage>(), "PostList");
    }

    [Fact]
    public void PageList_NamesItsSearchBox()
    {
        using var ctx = new AdminComponentHarness()
            .Route("pages", AdminComponentHarness.OnePage(Page()));

        Sweep(ctx.Render<PageListPage>(), "PageList");
    }

    [Fact]
    public void ConversationList_HasNoMouseOnlyRows()
    {
        using var ctx = new AdminComponentHarness()
            .Route("ai/conversations", Array.Empty<AiConversationSummaryDto>());

        Sweep(ctx.Render<ConversationListPage>(), "ConversationList");
    }

    [Fact]
    public void Dashboard_NamesItsAnalyticsDateRange()
    {
        using var ctx = new AdminComponentHarness()
            .Route("posts", AdminComponentHarness.OnePage(Post()))
            .Route("pages", AdminComponentHarness.OnePage(Page()))
            .Route("media", AdminComponentHarness.OnePage(File()));

        Sweep(ctx.Render<DashboardPage>(), "Dashboard");
    }

    // ── Editors ─────────────────────────────────────────────────────────────

    [Fact]
    public void PostEdit_NamesEveryFieldIncludingTheMarkdownBodyAndTags()
    {
        using var ctx = new AdminComponentHarness();

        Sweep(ctx.Render<PostEditPage>(), "PostEdit (new post)");
    }

    [Fact]
    public void PageEdit_NamesEveryField()
    {
        using var ctx = new AdminComponentHarness();

        Sweep(ctx.Render<PageEditPage>(), "PageEdit (new page)");
    }

    // ── Media ───────────────────────────────────────────────────────────────

    [Fact]
    public void MediaList_NamesItsSearchBoxAndUploadControl()
    {
        using var ctx = new AdminComponentHarness()
            .Route("media", AdminComponentHarness.OnePage(File()));

        Sweep(ctx.Render<MediaListPage>(), "MediaList");
    }

    /// <summary>
    /// The Upload control was a <c>&lt;label&gt;</c> wrapping an input hidden with
    /// <c>display:none</c>. A label is not focusable and a <c>display:none</c> input is removed from
    /// the tab order, so there was no sequence of keystrokes that could open the file dialog.
    /// </summary>
    [Fact]
    public void MediaList_UploadIsReachableFromTheKeyboard()
    {
        using var ctx = new AdminComponentHarness()
            .Route("media", AdminComponentHarness.OnePage(File()));

        var cut = ctx.Render<MediaListPage>();
        var upload = cut.Find("input[type=file]");

        upload.GetAttribute("style").Should().NotContain("display:none",
            "an input removed from the layout is also removed from the tab order");
        upload.GetAttribute("id").Should().NotBeNullOrWhiteSpace();

        var trigger = cut.Find($"label[for='{upload.GetAttribute("id")}']");
        trigger.TextContent.Trim().Should().NotBeEmpty("the trigger has to name what it does");
    }

    [Fact]
    public void MediaList_SelectingAFileIsAButton()
    {
        using var ctx = new AdminComponentHarness()
            .Route("media", AdminComponentHarness.OnePage(File()));

        var cut = ctx.Render<MediaListPage>();
        var card = cut.Find(".media-card");

        card.TagName.Should().Be("BUTTON", "selection is an action, so it belongs on a real control");
        card.GetAttribute("aria-pressed").Should().Be("false");

        card.Click();
        cut.Find(".media-card").GetAttribute("aria-pressed").Should().Be("true",
            "a toggle has to report its state, not only paint a border");
    }

    // ── AI chat ─────────────────────────────────────────────────────────────

    [Fact]
    public void ConversationChat_NamesItsComposerAndExportForm()
    {
        using var ctx = Chat();
        var cut = ctx.Render<ConversationChatPage>(p => p.Add(x => x.Id, ConversationId));

        Sweep(cut, "ConversationChat");

        cut.Find(".page-header .btn-primary").Click();
        Sweep(cut, "ConversationChat (export dialog open)");
    }

    /// <summary>
    /// The heading advertised "Click to rename" and carried the handler itself, so renaming was
    /// mouse-only.
    /// </summary>
    [Fact]
    public void ConversationChat_RenamingIsAButtonInsideTheHeading()
    {
        using var ctx = Chat();
        var cut = ctx.Render<ConversationChatPage>(p => p.Add(x => x.Id, ConversationId));

        var rename = cut.Find("h1 button");
        rename.GetAttribute("type").Should().Be("button");

        rename.Click();
        cut.Find(".chat-title-input").Should().NotBeNull("activating it opens the editor");
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    private static readonly Guid ConversationId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AdminComponentHarness Chat() =>
        new AdminComponentHarness().Route("ai/conversations/", new AiConversationDetailDto(
            ConversationId, "Draft ideas", DateTime.UtcNow, DateTime.UtcNow, null,
            [new AiMessageDto(Guid.NewGuid(), "user", "Hello", DateTime.UtcNow)]));

    private static void Next<T>(IRenderedComponent<T> cut) where T : IComponent =>
        // Back sits first in the nav once it exists, so Next/Finish is always the last button.
        cut.FindAll(".wizard-nav button")[^1].Click();

    private static BlogPostSummaryDto Post() => new(
        Guid.NewGuid(), "A post", "a-post", "Summary", true, true,
        DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, "Admin", []);

    private static PageDto Page() => new(
        Guid.NewGuid(), "A page", "a-page", "Body", true,
        DateTime.UtcNow, DateTime.UtcNow, null, null, null, null);

    private static MediaFileDto File() => new(
        Guid.NewGuid(), "A picture", "pic.png", "image/png", "/media/pic.png",
        2048, DateTime.UtcNow, "Admin");
}
