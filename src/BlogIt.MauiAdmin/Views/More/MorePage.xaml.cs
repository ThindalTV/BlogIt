namespace BlogIt.MauiAdmin.Views.More;

/// <summary>Flat menu shown as the phone tab bar's 5th tab, since a 10-item bottom
/// bar isn't viable on a phone. Routes to the same top-level Shell routes the
/// compact/desktop flyout exposes directly.</summary>
public partial class MorePage : ContentPage
{
    private static readonly Dictionary<string, string> Routes = new()
    {
        ["AI"] = "//ai",
        ["Redirects"] = "//redirects",
        ["Users"] = "//users",
        ["Settings"] = "//settings",
        ["My Account"] = "//account",
        ["Sites"] = "//sites",
    };

    public List<string> Items { get; } = [.. Routes.Keys];

    public MorePage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string label && Routes.TryGetValue(label, out var route))
            await Shell.Current.GoToAsync(route);
    }
}
