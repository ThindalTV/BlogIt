namespace BlogIt.MauiAdmin.Services;

/// <summary>
/// Which of the app's windows the user is working in, so a dialog or a navigation lands there.
/// </summary>
/// <remarks>
/// <c>Application.Current.Windows.FirstOrDefault()</c> — and <c>Shell.Current</c>, which is the
/// same lookup — always means the first window ever opened. With two windows on a desktop, a
/// delete confirmation raised in the second appeared in the first, where the user was not looking
/// and the action was waiting on an answer. MAUI has no public "is active" property, so this
/// follows each window's <see cref="Window.Activated"/> event instead.
/// </remarks>
public sealed class ActiveWindowTracker
{
    private WeakReference<Window>? _lastActivated;

    /// <summary>Starts following <paramref name="window"/>. Call once per window, as it is created.</summary>
    public void Track(Window window)
    {
        window.Activated += (_, _) => _lastActivated = new WeakReference<Window>(window);
        _lastActivated ??= new WeakReference<Window>(window);
    }

    /// <summary>The window activated most recently and still open, else the first open one.</summary>
    public Window? CurrentWindow
    {
        get
        {
            var windows = Application.Current?.Windows;
            if (windows is null)
                return null;

            if (_lastActivated?.TryGetTarget(out var window) == true && windows.Contains(window))
                return window;

            return windows.FirstOrDefault();
        }
    }

    /// <summary>
    /// The page a dialog should be raised on: the top modal of the current window if one is open,
    /// else its root page. Raising it on the root under an open modal can leave the alert behind
    /// the modal on some platforms.
    /// </summary>
    public Page? CurrentPage
    {
        get
        {
            var root = CurrentWindow?.Page;
            return root?.Navigation.ModalStack.LastOrDefault() ?? root;
        }
    }
}
