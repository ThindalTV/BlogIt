using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;

namespace BlogIt.Tests.Helpers;

/// <summary>
/// Assertions over rendered admin markup for the accessibility rules that apply to every screen,
/// written as sweeps rather than one assertion per field: the defect these guard against is a new
/// input or a new clickable <c>div</c> being added without the surrounding plumbing, which a
/// hand-listed set of ids would not notice.
/// </summary>
public static class AdminAccessibility
{
    /// <summary>
    /// Controls that take an accessible name from a label. Buttons and submits are excluded — they
    /// name themselves from their content — and hidden inputs are not exposed at all.
    /// </summary>
    private const string NameableControls =
        "input:not([type=hidden]):not([type=button]):not([type=submit]):not([type=reset]), select, textarea";

    /// <summary>
    /// Elements allowed to carry a click handler without being a real control. Both are dismissal
    /// scrims: clicking them repeats an action that is also on a focusable button (the dialog's
    /// Cancel/✕, the sidebar's toggle) and is reachable with Escape, so a keyboard or screen-reader
    /// user never needs them and exposing them as controls would only add a nameless tab stop.
    /// </summary>
    private static readonly string[] DismissScrims = ["modal-overlay", "sidebar-overlay"];

    /// <summary>Tags that are focusable and clickable without any extra attributes.</summary>
    private static readonly string[] NativeControls =
        ["BUTTON", "A", "INPUT", "SELECT", "TEXTAREA", "SUMMARY", "OPTION"];

    /// <summary>
    /// Every <c>&lt;label&gt;</c> resolves to a control, and every control that needs a name has
    /// one — via <c>for</c>/<c>id</c>, by wrapping, or through <c>aria-label(ledby)</c>.
    /// </summary>
    public static void AssertControlsAreLabelled<T>(IRenderedComponent<T> cut, string screen)
        where T : IComponent
    {
        var ids = cut.FindAll("[id]")
            .Select(e => e.GetAttribute("id")!)
            .ToHashSet(StringComparer.Ordinal);

        var labelledIds = cut.FindAll("label[for]")
            .Select(e => e.GetAttribute("for")!)
            .ToHashSet(StringComparer.Ordinal);

        var problems = new List<string>();

        foreach (var label in cut.FindAll("label"))
        {
            var target = label.GetAttribute("for");
            if (target is null)
            {
                // A label wrapping its control is associated implicitly and needs no `for`.
                if (label.QuerySelector(NameableControls) is null)
                    problems.Add($"label \"{Text(label)}\" has no for= and wraps no control");
            }
            else if (!ids.Contains(target))
            {
                problems.Add($"label \"{Text(label)}\" points at for=\"{target}\", which no element has");
            }
        }

        foreach (var control in cut.FindAll(NameableControls))
        {
            if (HasAccessibleName(control, ids, labelledIds))
                continue;

            problems.Add($"unlabelled control {Describe(control)}");
        }

        problems.Should().BeEmpty(
            "every control on {0} must be reachable by its name — a password box with no associated "
            + "label also defeats password managers", screen);
    }

    /// <summary>
    /// No element carries a click handler unless activating it from the keyboard works: a real
    /// control, or something that has taken on a role, a tab stop and its own key handling.
    /// </summary>
    public static void AssertClickTargetsAreControls<T>(IRenderedComponent<T> cut, string screen)
        where T : IComponent
    {
        var problems = cut.FindAll("*")
            .Where(HasClickHandler)
            .Where(e => !IsKeyboardOperable(e))
            .Select(e => Describe(e))
            .ToList();

        problems.Should().BeEmpty(
            "a click handler on {0} that only a mouse can reach makes the action impossible from the "
            + "keyboard", screen);
    }

    /// <summary>The dialog contract: a named, modal <c>role="dialog"</c>.</summary>
    public static void AssertIsLabelledModalDialog(IElement dialog, IReadOnlyCollection<IElement> withIds)
    {
        dialog.GetAttribute("role").Should().Be("dialog");
        dialog.GetAttribute("aria-modal").Should().Be("true");

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrWhiteSpace("a dialog announces itself by its title");
        withIds.Select(e => e.GetAttribute("id")).Should().Contain(labelledBy,
            "aria-labelledby has to point at the element holding the dialog's title");
    }

    private static bool HasAccessibleName(
        IElement control, HashSet<string> ids, HashSet<string> labelledIds)
    {
        if (!string.IsNullOrWhiteSpace(control.GetAttribute("aria-label")))
            return true;

        var labelledBy = control.GetAttribute("aria-labelledby");
        if (labelledBy is not null && ids.Contains(labelledBy))
            return true;

        var id = control.GetAttribute("id");
        if (id is not null && labelledIds.Contains(id))
            return true;

        return control.Closest("label") is not null;
    }

    private static bool HasClickHandler(IElement element) =>
        // Blazor renders an assigned @onclick as the attribute `blazor:onclick`. Matched exactly:
        // `@onclick:stopPropagation` renders as `blazor:onclick:stopPropagation` on elements that
        // have no handler of their own.
        element.Attributes.Any(a => string.Equals(a.Name, "blazor:onclick", StringComparison.Ordinal));

    private static bool IsKeyboardOperable(IElement element)
    {
        if (NativeControls.Contains(element.TagName, StringComparer.Ordinal))
            return true;

        if (DismissScrims.Any(element.ClassList.Contains))
            return true;

        // Anything else has to opt in fully: a role to be announced as, a tab stop to be reached
        // by, and a key handler to be activated with. Two out of three is still unusable.
        var hasTabStop = int.TryParse(element.GetAttribute("tabindex"), out var index) && index >= 0;
        var hasRole = !string.IsNullOrWhiteSpace(element.GetAttribute("role"));
        var hasKeyHandler = element.Attributes.Any(a =>
            a.Name is "blazor:onkeydown" or "blazor:onkeyup" or "blazor:onkeypress");

        return hasTabStop && hasRole && hasKeyHandler;
    }

    private static string Describe(IElement element)
    {
        var id = element.GetAttribute("id");
        var type = element.GetAttribute("type");
        var name = element.TagName.ToLowerInvariant()
            + (type is null ? "" : $"[type={type}]")
            + (id is null ? "" : $"#{id}")
            + (element.ClassList.Length == 0 ? "" : "." + string.Join('.', element.ClassList));

        var text = Text(element);
        return text.Length == 0 ? name : $"{name} (“{text}”)";
    }

    private static string Text(IElement element)
    {
        var text = element.TextContent.Trim().ReplaceLineEndings(" ");
        return text.Length > 60 ? text[..60] + "…" : text;
    }
}
