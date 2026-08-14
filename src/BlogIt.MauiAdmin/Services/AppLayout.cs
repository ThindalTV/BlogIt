namespace BlogIt.MauiAdmin.Services;

public enum LayoutMode { Phone, Compact, DesktopWide }

/// <summary>
/// Decides which Shell layout family to use. This is a structural fork (tab bar vs.
/// locked nav rail), not a responsive breakpoint: Windows always gets the wide desktop
/// layout regardless of window size, and phones always get the compact layout.
/// </summary>
public static class AppLayout
{
    public static LayoutMode Mode { get; } =
        DeviceInfo.Platform == DevicePlatform.WinUI ? LayoutMode.DesktopWide
        : DeviceInfo.Idiom == DeviceIdiom.Phone ? LayoutMode.Phone
        : LayoutMode.Compact; // Tablet, MacCatalyst, Unknown
}
