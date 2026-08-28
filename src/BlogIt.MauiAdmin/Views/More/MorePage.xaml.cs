using BlogIt.MauiAdmin.Core.Navigation;
using BlogIt.MauiAdmin.Services;

namespace BlogIt.MauiAdmin.Views.More;

/// <summary>Flat menu shown as the phone tab bar's 5th tab, since a 10-item bottom bar isn't
/// viable on a phone. Also hosts the site switcher (see MorePage.xaml), because the phone
/// Shell has no flyout to pin it to.</summary>
public partial class MorePage : ContentPage
{
    private readonly DestinationRouter _router = ServiceHelper.GetRequiredService<DestinationRouter>();

    /// <summary>Menu labels in catalog order, translated back to a route on tap.</summary>
    public List<string> Items { get; } = [.. AppNavigation.Secondary.Select(d => d.Label)];

    public MorePage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string label) return;

        var destination = AppNavigation.Secondary.FirstOrDefault(d => d.Label == label);
        if (destination is null) return;

        // Routed rather than hard-coded: an absolute //route to any of these throws on the
        // phone Shell, whose hierarchy contains only the five tabs.
        await Shell.Current.GoToAsync(_router.RouteTo(destination.Key));
    }
}
