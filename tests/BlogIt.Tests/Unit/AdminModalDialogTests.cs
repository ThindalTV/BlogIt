using BlogIt.Admin.Shared;
using BlogIt.Shared.DTOs;
using BlogIt.Tests.Helpers;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

using ConversationChatPage = BlogIt.Admin.Pages.Ai.ConversationChat;

namespace BlogIt.Tests.Unit;

/// <summary>
/// The admin's three modals were plain nested <c>div</c>s: no <c>role="dialog"</c>, no
/// <c>aria-modal</c>, nothing moving focus in or putting it back, no Escape, and no trap keeping Tab
/// inside. To a screen reader they were an unannounced block of content in the middle of a page that
/// was still fully reachable behind them.
/// </summary>
public class AdminModalDialogTests
{
    // ── The shared shell ────────────────────────────────────────────────────

    [Fact]
    public void ConfirmDialog_IsAnAriaModalDialogNamedByItsTitle()
    {
        using var ctx = new AdminComponentHarness();
        var cut = Confirm(ctx);

        AdminAccessibility.AssertIsLabelledModalDialog(cut.Find("[role=dialog]"), cut.FindAll("[id]"));
        cut.Find($"#{cut.Find("[role=dialog]").GetAttribute("aria-labelledby")}")
            .TextContent.Should().Be("Delete File");
    }

    [Fact]
    public void MediaPicker_IsAnAriaModalDialogNamedByItsTitle()
    {
        using var ctx = Picker();
        var cut = ctx.Render<MediaPicker>(p => p.Add(x => x.IsOpen, true));

        AdminAccessibility.AssertIsLabelledModalDialog(cut.Find("[role=dialog]"), cut.FindAll("[id]"));
    }

    [Fact]
    public void ExportDialog_IsAnAriaModalDialog()
    {
        using var ctx = new AdminComponentHarness().Route("ai/conversations/", Conversation);
        var cut = ctx.Render<ConversationChatPage>(p => p.Add(x => x.Id, Conversation.Id));
        cut.Find(".page-header .btn-primary").Click();

        AdminAccessibility.AssertIsLabelledModalDialog(cut.Find("[role=dialog]"), cut.FindAll("[id]"));
    }

    // ── Escape ──────────────────────────────────────────────────────────────

    [Fact]
    public void ConfirmDialog_EscapeCancelsWithoutConfirming()
    {
        using var ctx = new AdminComponentHarness();
        var cancelled = 0;
        var confirmed = 0;

        var cut = ctx.Render<ConfirmDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Delete File")
            .Add(x => x.OnCancel, () => cancelled++)
            .Add(x => x.OnConfirm, () => confirmed++));

        cut.Find("[role=dialog]").KeyDown(Key.Escape);

        cancelled.Should().Be(1);
        confirmed.Should().Be(0, "Escape dismisses; it never takes the destructive branch");
    }

    [Fact]
    public void MediaPicker_EscapeCloses()
    {
        using var ctx = Picker();
        var closed = 0;

        var cut = ctx.Render<MediaPicker>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnClose, () => closed++));

        cut.Find("[role=dialog]").KeyDown(Key.Escape);

        closed.Should().Be(1);
    }

    [Fact]
    public void ConfirmDialog_OtherKeysDoNotDismissIt()
    {
        using var ctx = new AdminComponentHarness();
        var cancelled = 0;

        var cut = ctx.Render<ConfirmDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.OnCancel, () => cancelled++));

        cut.Find("[role=dialog]").KeyDown(Key.Enter);
        cut.Find("[role=dialog]").KeyDown(Key.Down);

        cancelled.Should().Be(0);
    }

    // ── Focus ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Opening moves focus onto the dialog itself, which is why it carries <c>tabindex="-1"</c>:
    /// without that a programmatic focus call is a no-op and the caret stays behind the overlay.
    /// </summary>
    [Fact]
    public void ConfirmDialog_MovesFocusIntoTheDialogWhenItOpens()
    {
        using var ctx = new AdminComponentHarness();
        var cut = Confirm(ctx);

        var dialog = cut.Find("[role=dialog]");
        dialog.GetAttribute("tabindex").Should().Be("-1");
        ctx.JSInterop.VerifyFocusAsyncInvoke().Arguments[0].ShouldBeElementReferenceTo(dialog);
    }

    /// <summary>
    /// Wrapping Tab and putting focus back where it came from both need the document's list of
    /// focusable elements, which Blazor cannot enumerate — so the component delegates to
    /// <c>blogitInterop.modal</c>. What is asserted here is that it asks; that the JS then does it
    /// correctly is not something a headless renderer can show.
    /// </summary>
    [Fact]
    public void ConfirmDialog_InstallsTheFocusTrapOnOpenAndReleasesItOnClose()
    {
        using var ctx = new AdminComponentHarness();
        var cut = Confirm(ctx);

        ctx.JSInterop.VerifyInvoke("blogitInterop.modal.open");
        ctx.JSInterop.VerifyNotInvoke("blogitInterop.modal.close");

        cut.Render(p => p.Add(x => x.IsOpen, false).Add(x => x.Title, "Delete File"));

        ctx.JSInterop.VerifyInvoke("blogitInterop.modal.close");
    }

    [Fact]
    public void ClosedDialog_DoesNotTouchFocusAtAll()
    {
        using var ctx = new AdminComponentHarness();

        ctx.Render<ConfirmDialog>(p => p.Add(x => x.IsOpen, false));

        ctx.JSInterop.VerifyNotInvoke("blogitInterop.modal.open");
    }

    // ── The close affordance ────────────────────────────────────────────────

    [Fact]
    public void MediaPicker_CloseButtonHasANameBeyondItsGlyph()
    {
        using var ctx = Picker();
        var cut = ctx.Render<MediaPicker>(p => p.Add(x => x.IsOpen, true));

        var close = cut.Find(".modal-close");
        close.TagName.Should().Be("BUTTON");
        close.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace(
            "✕ is punctuation, so the glyph alone announces nothing");
    }

    /// <summary>
    /// Selection in the picker grid was a <c>div</c> with <c>@onclick</c>: no tab stop, no role, and
    /// no way to press it.
    /// </summary>
    [Fact]
    public void MediaPicker_SelectingAThumbnailIsAButton()
    {
        using var ctx = Picker();
        var cut = ctx.Render<MediaPicker>(p => p.Add(x => x.IsOpen, true));

        var thumb = cut.Find(".media-thumb");
        thumb.TagName.Should().Be("BUTTON");
        thumb.GetAttribute("aria-pressed").Should().Be("false");

        thumb.Click();
        cut.Find(".media-thumb").GetAttribute("aria-pressed").Should().Be("true");
    }

    // ── Fixtures ────────────────────────────────────────────────────────────

    private static readonly AiConversationDetailDto Conversation = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"), "Draft ideas",
        DateTime.UtcNow, DateTime.UtcNow, null, []);

    private static IRenderedComponent<ConfirmDialog> Confirm(AdminComponentHarness ctx) =>
        ctx.Render<ConfirmDialog>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Delete File"));

    private static AdminComponentHarness Picker() =>
        new AdminComponentHarness().Route("media", AdminComponentHarness.OnePage(new MediaFileDto(
            Guid.NewGuid(), "A picture", "pic.png", "image/png", "/media/pic.png",
            2048, DateTime.UtcNow, "Admin")));
}
