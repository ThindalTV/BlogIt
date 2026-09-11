using Microsoft.AspNetCore.Components;

namespace BlogIt.Components.Shared;

/// <summary>
/// One component's contribution to the document head, held as a mutable slot.
/// </summary>
/// <remarks>
/// A slot rather than a bare <see cref="RenderFragment"/> because a component hands the registry a
/// new <see cref="RenderFragment"/> delegate on every re-render: registering the delegate itself
/// would either duplicate the entry each render or leave the registry holding a stale one. The
/// contributing component owns exactly one slot for its lifetime and rewrites
/// <see cref="Content"/> in place.
/// </remarks>
public sealed class BlogItHeadSlot
{
    /// <summary>The markup this contributor currently wants in the head. May be <see langword="null"/>.</summary>
    public RenderFragment? Content { get; set; }
}

/// <summary>
/// Collects the head markup every component on the page wants to contribute, so that a single
/// <see cref="BlogItHeadOutlet"/> can emit all of it inside one <c>&lt;HeadContent&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// This type exists because Blazor's head section keeps only the <em>last</em>
/// <c>&lt;HeadContent&gt;</c> that renders: two components each declaring their own block do not
/// combine, the second silently replaces the first. A public page rendering both
/// <c>&lt;SeoHead&gt;</c> and <c>&lt;GaScript&gt;</c> therefore shipped the analytics tag and lost
/// every Open Graph, Twitter, canonical and structured-data tag with it — no error, no log, and the
/// page still rendered.
/// </para>
/// <para>
/// Registered <b>scoped</b>, deliberately, and not as a process-wide singleton: the contents of the
/// head belong to one page render. A singleton instance is shared by every request the application
/// is serving at once, so concurrent visitors would append into the same list — one reader's
/// <c>og:title</c> would appear on another's page, and the list would grow without bound for the
/// lifetime of the process. Scoped gives exactly one instance per request, which is the "one shared
/// instance" this design needs.
/// </para>
/// <para>
/// No locking, for the same reason: a scoped instance is only ever touched by the single render
/// loop of its own request.
/// </para>
/// </remarks>
public interface IBlogItHeadRegistry
{
    /// <summary>The contributions registered so far, in the order they were added.</summary>
    IReadOnlyList<BlogItHeadSlot> Slots { get; }

    /// <summary>Raised whenever the set of contributions, or the content of one, changes.</summary>
    event Action? Changed;

    /// <summary>Adds a contributor's slot.</summary>
    void Add(BlogItHeadSlot slot);

    /// <summary>Removes a contributor's slot, when that component is disposed.</summary>
    void Remove(BlogItHeadSlot slot);

    /// <summary>Signals that an already-registered slot's content was rewritten.</summary>
    void NotifyChanged();
}

/// <inheritdoc cref="IBlogItHeadRegistry"/>
internal sealed class BlogItHeadRegistry : IBlogItHeadRegistry
{
    private readonly List<BlogItHeadSlot> slots = [];

    public IReadOnlyList<BlogItHeadSlot> Slots => slots;

    public event Action? Changed;

    public void Add(BlogItHeadSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        slots.Add(slot);
        Changed?.Invoke();
    }

    public void Remove(BlogItHeadSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        if (slots.Remove(slot))
            Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();
}
